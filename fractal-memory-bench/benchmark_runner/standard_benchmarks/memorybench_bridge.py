from __future__ import annotations

import json
import logging
import os
import re
import shutil
import subprocess
from pathlib import Path

from benchmark_runner.adapters.base import MemoryApproachAdapter
from benchmark_runner.standard_benchmarks.datasets import normalize_benchmark_name
from benchmark_runner.standard_benchmarks.memorybench_provider_wrapper import MemoryBenchProviderWrapper
from benchmark_runner.standard_benchmarks.models import (
    MemoryBenchAvailabilityResult,
    MemoryBenchRunConfig,
    MemoryBenchRunResult,
)
from benchmark_runner.standard_benchmarks.result_normalizer import normalize_run_directory, write_normalized_outputs

logger = logging.getLogger(__name__)


class MemoryBenchBridge:
    def is_available(self, config: MemoryBenchRunConfig) -> MemoryBenchAvailabilityResult:
        warnings: list[str] = []
        errors: list[str] = []
        working_dir = config.working_dir.resolve()
        if not working_dir.exists():
            errors.append(f"MemoryBench working_dir does not exist: {working_dir}")
        executable_path = self._resolve_executable(config.executable)
        if executable_path is None:
            errors.append(f"Executable '{config.executable}' was not found on PATH.")
        elif not self._looks_like_memorybench_repo(working_dir):
            errors.append(f"Working directory does not look like a MemoryBench checkout: {working_dir}")
        else:
            warnings.extend(self._run_probe(executable_path, working_dir))
        return MemoryBenchAvailabilityResult(
            available=not errors,
            warnings=warnings,
            errors=errors,
            executable_path=str(executable_path) if executable_path else None,
            working_dir=str(working_dir),
        )

    def validate_environment(self, config: MemoryBenchRunConfig) -> list[str]:
        issues: list[str] = []
        if config.mode != "subprocess":
            issues.append(f"Unsupported MemoryBench mode '{config.mode}'. Only subprocess is supported.")
        try:
            normalize_benchmark_name(config.benchmark)
        except ValueError as exc:
            issues.append(str(exc))
        if not config.evaluator:
            issues.append("MemoryBench evaluator is required.")
        for key, value in config.environment.items():
            missing_vars = self._find_unresolved_env_vars(value)
            if missing_vars:
                issues.append(
                    f"MemoryBench environment value for '{key}' references unset variables: {', '.join(missing_vars)}"
                )
        return issues

    def run(
        self,
        approach_name: str,
        adapter: MemoryApproachAdapter,
        run_config: MemoryBenchRunConfig,
        output_dir: str,
    ) -> MemoryBenchRunResult:
        benchmark = normalize_benchmark_name(run_config.benchmark)
        base_dir = Path(output_dir) / benchmark / approach_name
        raw_dir = base_dir / "raw"
        normalized_dir = base_dir / "normalized"
        base_dir.mkdir(parents=True, exist_ok=True)
        raw_dir.mkdir(parents=True, exist_ok=True)
        normalized_dir.mkdir(parents=True, exist_ok=True)
        stdout_log = base_dir / "stdout.log"
        stderr_log = base_dir / "stderr.log"
        provider_manifest = MemoryBenchProviderWrapper(approach_name, adapter).write_metadata(base_dir)
        warnings = self.validate_environment(run_config)
        availability = self.is_available(run_config)
        warnings.extend(availability.warnings)
        run_id = self._infer_run_id(base_dir)

        if warnings and run_config.skip_if_unavailable:
            return MemoryBenchRunResult(
                status="skipped_misconfigured",
                run_id=run_id,
                benchmark=benchmark,
                approach=approach_name,
                evaluator=run_config.evaluator,
                output_dir=str(base_dir),
                stdout_log_path=str(stdout_log),
                stderr_log_path=str(stderr_log),
                warnings=warnings,
                error="; ".join(warnings),
            )

        if not availability.available:
            if run_config.skip_if_unavailable:
                return MemoryBenchRunResult(
                    status="skipped_unavailable",
                    run_id=run_id,
                    benchmark=benchmark,
                    approach=approach_name,
                    evaluator=run_config.evaluator,
                    output_dir=str(base_dir),
                    stdout_log_path=str(stdout_log),
                    stderr_log_path=str(stderr_log),
                    warnings=warnings + availability.errors,
                    error=None,
                )
            return MemoryBenchRunResult(
                status="skipped_misconfigured",
                run_id=run_id,
                benchmark=benchmark,
                approach=approach_name,
                evaluator=run_config.evaluator,
                output_dir=str(base_dir),
                stdout_log_path=str(stdout_log),
                stderr_log_path=str(stderr_log),
                warnings=warnings,
                error="; ".join(availability.errors),
            )

        command = self.build_command(run_config, run_id, provider_manifest)
        env = self._build_environment(run_config.environment)
        try:
            completed = subprocess.run(
                command,
                cwd=run_config.working_dir,
                env=env,
                capture_output=True,
                text=True,
                timeout=run_config.timeout_seconds,
                check=False,
            )
        except subprocess.TimeoutExpired as exc:
            stdout_log.write_text(exc.stdout or "", encoding="utf-8")
            stderr_log.write_text(exc.stderr or "", encoding="utf-8")
            return MemoryBenchRunResult(
                status="failed",
                run_id=run_id,
                benchmark=benchmark,
                approach=approach_name,
                evaluator=run_config.evaluator,
                command=command,
                output_dir=str(base_dir),
                stdout_log_path=str(stdout_log),
                stderr_log_path=str(stderr_log),
                warnings=warnings,
                error=f"MemoryBench subprocess timed out after {run_config.timeout_seconds} seconds.",
            )

        stdout_log.write_text(completed.stdout or "", encoding="utf-8")
        stderr_log.write_text(completed.stderr or "", encoding="utf-8")
        raw_files = self._collect_raw_result_files(raw_dir, run_config, run_id)
        if completed.returncode != 0:
            return MemoryBenchRunResult(
                status="failed",
                run_id=run_id,
                benchmark=benchmark,
                approach=approach_name,
                evaluator=run_config.evaluator,
                command=command,
                output_dir=str(base_dir),
                stdout_log_path=str(stdout_log),
                stderr_log_path=str(stderr_log),
                raw_result_files=raw_files,
                warnings=warnings,
                error=f"MemoryBench subprocess exited with code {completed.returncode}.",
            )

        expected_report = self._resolve_memorybench_run_dir(run_config, run_id) / "report.json"
        if not expected_report.exists():
            return MemoryBenchRunResult(
                status="failed",
                run_id=run_id,
                benchmark=benchmark,
                approach=approach_name,
                evaluator=run_config.evaluator,
                command=command,
                output_dir=str(base_dir),
                stdout_log_path=str(stdout_log),
                stderr_log_path=str(stderr_log),
                raw_result_files=raw_files,
                warnings=warnings,
                error=f"MemoryBench run did not produce report.json at {expected_report}.",
            )

        try:
            normalized_run, normalization_warnings = normalize_run_directory(
                expected_report.parent,
                run_id=run_id,
                benchmark=benchmark,
                approach=approach_name,
                evaluator=run_config.evaluator,
            )
        except FileNotFoundError as exc:
            return MemoryBenchRunResult(
                status="failed",
                run_id=run_id,
                benchmark=benchmark,
                approach=approach_name,
                evaluator=run_config.evaluator,
                command=command,
                output_dir=str(base_dir),
                stdout_log_path=str(stdout_log),
                stderr_log_path=str(stderr_log),
                raw_result_files=raw_files,
                warnings=warnings,
                error=str(exc),
            )

        warnings.extend(normalization_warnings)
        paths = write_normalized_outputs(normalized_run, normalized_dir)
        if not run_config.preserve_raw_outputs:
            shutil.rmtree(raw_dir)
            raw_files = []
        return MemoryBenchRunResult(
            status="succeeded",
            run_id=run_id,
            benchmark=benchmark,
            approach=approach_name,
            evaluator=run_config.evaluator,
            command=command,
            output_dir=str(base_dir),
            stdout_log_path=str(stdout_log),
            stderr_log_path=str(stderr_log),
            raw_result_files=raw_files,
            normalized_json_path=str(paths["normalized_json"]),
            normalized_csv_path=str(paths["normalized_csv"]),
            summary_json_path=str(paths["summary_json"]),
            summary_csv_path=str(paths["summary_csv"]),
            questions_json_path=str(paths["questions_json"]),
            questions_csv_path=str(paths["questions_csv"]),
            search_hits_json_path=str(paths["search_hits_json"]),
            search_hits_csv_path=str(paths["search_hits_csv"]),
            summary=normalized_run.summary,
            warnings=warnings,
        )

    def build_command(self, config: MemoryBenchRunConfig, run_id: str, provider_manifest: Path) -> list[str]:
        executable = self._resolve_executable(config.executable) or config.executable
        command = [
            executable,
            "run",
            "src/index.ts",
            "run",
            "-p",
            config.provider,
            "-b",
            normalize_benchmark_name(config.benchmark),
            "-j",
            config.evaluator,
            "-r",
            run_id,
        ]
        if config.answering_model:
            command.extend(["-m", config.answering_model])
        if config.limit is not None:
            command.extend(["-l", str(config.limit)])
        command.extend(["--force"])
        command.extend(["--provider-manifest", str(provider_manifest)])
        return command

    @staticmethod
    def _resolve_executable(executable: str) -> str | None:
        if Path(executable).expanduser().exists():
            return str(Path(executable).expanduser().resolve())
        return shutil.which(executable)

    @staticmethod
    def _looks_like_memorybench_repo(working_dir: Path) -> bool:
        if not working_dir.exists():
            return False
        marker_files = any((working_dir / candidate).exists() for candidate in ("package.json", "bun.lock", "README.md"))
        marker_dirs = any((working_dir / candidate).exists() for candidate in ("benchmarks", "src", "packages"))
        return marker_files and marker_dirs

    @staticmethod
    def _run_probe(executable_path: str, working_dir: Path) -> list[str]:
        try:
            subprocess.run(
                [executable_path, "--version"],
                cwd=working_dir,
                capture_output=True,
                text=True,
                timeout=10,
                check=False,
            )
        except OSError as exc:
            return [f"Executable probe failed: {exc}"]
        except subprocess.TimeoutExpired:
            return ["Executable probe timed out."]
        return []

    @staticmethod
    def _build_environment(configured_environment: dict[str, str]) -> dict[str, str]:
        env = os.environ.copy()
        for key, value in configured_environment.items():
            env[key] = MemoryBenchBridge._expand_env_value(value)
        return env

    @staticmethod
    def _resolve_memorybench_run_dir(config: MemoryBenchRunConfig, run_id: str) -> Path:
        return config.working_dir / "data" / "runs" / run_id

    def _collect_raw_result_files(self, raw_dir: Path, config: MemoryBenchRunConfig, run_id: str) -> list[str]:
        candidates = [raw_dir, self._resolve_memorybench_run_dir(config, run_id)]
        files: list[str] = []
        for candidate in candidates:
            if candidate.exists():
                files.extend(str(path) for path in candidate.rglob("*") if path.is_file())
        return sorted(set(files))

    @staticmethod
    def _expand_env_value(value: str) -> str:
        pattern = re.compile(r"\$\{([^}]+)\}")

        def replace(match: re.Match[str]) -> str:
            return os.environ.get(match.group(1), "")

        return pattern.sub(replace, value)

    @staticmethod
    def _find_unresolved_env_vars(value: str) -> list[str]:
        pattern = re.compile(r"\$\{([^}]+)\}")
        missing: list[str] = []
        for match in pattern.finditer(value):
            name = match.group(1)
            if not os.environ.get(name):
                missing.append(name)
        return missing

    @staticmethod
    def _infer_run_id(path: Path) -> str:
        for candidate in path.parents:
            if candidate.name.startswith("run-"):
                return candidate.name
        return path.name


def write_standard_benchmark_manifest(results: list[MemoryBenchRunResult], path: str | Path) -> Path:
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps([result.model_dump(mode="json") for result in results], indent=2), encoding="utf-8")
    return target
