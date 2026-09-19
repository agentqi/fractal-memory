#!/usr/bin/env python3
"""Exercise installed CLI/MCP workflows against disposable local memory."""
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading

cli, server = map(lambda value: str(Path(value).resolve()), sys.argv[1:3])
with tempfile.TemporaryDirectory(prefix="fractalmem-workflows-") as directory:
    def fm(*arguments):
        result = subprocess.run([cli, *arguments, "--json"], cwd=directory, capture_output=True, text=True, timeout=30)
        if result.returncode:
            raise AssertionError(result.stderr)
        return json.loads(result.stdout)

    fm("init")
    fm("node", "create", "projects/workflow")
    original = fm("read", "projects/workflow")
    writers = [subprocess.Popen([cli, "update", "projects/workflow", "Current Objective", name,
                                "--expected-hash", original["hash"], "--json"], cwd=directory,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
               for name in ["First process", "Second process"]]
    for writer in writers:
        writer.communicate(timeout=30)
    assert sorted(writer.returncode for writer in writers) == [0, 2], "Concurrent updates must reject one stale writer"

    env = {**os.environ, "FRACTALMEM_REPOSITORY_ROOT": directory}
    with tempfile.TemporaryFile(mode="w+") as errors:
        process = subprocess.Popen([server], cwd=directory, env=env, stdin=subprocess.PIPE,
                                   stdout=subprocess.PIPE, stderr=errors, text=True, bufsize=1)
        responses = queue.Queue()
        def consume():
            for line in process.stdout:
                responses.put(json.loads(line))
        threading.Thread(target=consume, daemon=True).start()
        sequence = 0
        def rpc(method, params):
            global sequence
            sequence += 1
            process.stdin.write(json.dumps({"jsonrpc": "2.0", "id": sequence, "method": method, "params": params}) + "\n")
            process.stdin.flush()
            while True:
                reply = responses.get(timeout=30)
                if reply.get("id") == sequence:
                    assert "error" not in reply, reply
                    return reply["result"]

        def call(name, **arguments):
            return rpc("tools/call", {"name": name, "arguments": arguments})

        def data(result):
            assert not result.get("isError"), result
            return result["structuredContent"]

        try:
            rpc("initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "workflow-smoke", "version": "1"}})
            process.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n')
            process.stdin.flush()
            names = {tool["name"] for tool in rpc("tools/list", {})["tools"]}
            assert {"memory_update", "memory_context", "memory_import", "memory_resume", "memory_read", "memory_doctor"} <= names
            read = call("memory_read", path="projects/workflow")
            original = data(read)
            link = next(block for block in read["content"] if block["type"] == "resource_link")
            resource = rpc("resources/read", {"uri": link["uri"]})
            assert resource["contents"][0]["text"] == original["content"]
            updated = data(call("memory_update", path="projects/workflow", section="Current Objective",
                                content="Verify MCP source access and safe updates.", expectedHash=original["hash"]))
            assert updated["document"]["hash"] != original["hash"]
            stale = call("memory_update", path="projects/workflow", section="Current Objective", content="Stale write", expectedHash=original["hash"])
            assert stale["isError"] and "changed since" in stale["content"][0]["text"]
            for name in ["memory_list", "memory_attention"]:
                data(call(name, scope="projects"))
            decisions = data(call("memory_read", path="projects/workflow", file="decisions"))
            decision = data(call("memory_append", path="projects/workflow", file="decisions", content="Keep source files authoritative.", expectedHash=decisions["hash"]))
            assert decision["decisionId"]
            data(call("memory_decisions", path="projects/workflow"))
            index = data(call("memory_read", path="projects/workflow", file="index"))
            data(call("memory_review", path="projects/workflow", reviewAfter="2099-01-01T00:00:00Z", expectedHash=index["hash"], status="Active"))
            timeline = data(call("memory_read", path="projects/workflow", file="timeline"))
            data(call("memory_append", path="projects/workflow", file="timeline", content="MCP workflow verified.", expectedHash=timeline["hash"]))
            packed = data(call("memory_context", path="projects/workflow", maxCharacters=400))
            assert len(packed["text"]) <= 400
            assert packed["usedCharacters"] <= 400
            data(call("memory_handoff_create", path="projects/workflow"))
            resumed = data(call("memory_resume", path="projects/workflow"))
            assert resumed["comparisonAvailable"] and resumed["changedFiles"] == []
            data(call("memory_handoff_list", path="projects/workflow"))
            assert not call("memory_handoff_read", path="projects/workflow").get("isError")
            data(call("memory_node_create", path="projects/created-via-mcp"))
            imported = data(call("memory_import", path="research/imported", sourceName="notes.md", content="# Original\nImported source.", apply=False))
            assert imported["canApply"] and not imported["applied"]
            assert not (Path(directory) / ".fractal-memory/research/imported").exists()
            data(call("memory_import", path="research/imported", sourceName="notes.md", content="# Original\nImported source.", apply=True))
            artifact = call("memory_read", path="research/imported", file="artifacts/source.md")
            artifact_link = next(block for block in artifact["content"] if block["type"] == "resource_link")
            assert rpc("resources/read", {"uri": artifact_link["uri"]})["contents"][0]["text"] == "# Original\nImported source."
            assert data(call("memory_doctor", repair=True))["indexesRebuilt"]
            denied = call("memory_read", path="projects/workflow", file="artifacts/../../config.yaml")
            assert denied["isError"]
        except BaseException:
            errors.seek(0)
            print(errors.read(), file=sys.stderr)
            raise
        finally:
            process.stdin.close()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
print("Workflow smoke tests passed: installed CLI, concurrent processes, MCP structured data and resource links.")
