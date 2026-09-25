#!/usr/bin/env python3
"""Build and install the current checkout into a fresh, isolated .NET tool directory."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET


def main():
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tool-dir', type=Path, default=root / '.tools' / 'fractalmem')
    args = parser.parse_args()
    destination = args.tool_dir.expanduser().resolve()
    if destination.exists():
        parser.error(f'Tool directory already exists: {destination}. Delete it to reinstall, or choose a new --tool-dir.')
    if not shutil.which('dotnet'):
        parser.error('Install the .NET 10 SDK first: https://dotnet.microsoft.com/download/dotnet/10.0')
    projects = ['FractalMemory.Cli', 'FractalMemory.McpServer']
    with tempfile.TemporaryDirectory(prefix='fractalmem-install-') as temporary:
        packages = Path(temporary) / 'packages'
        versions = {}
        for project in projects:
            subprocess.run(['dotnet', 'pack', str(root / 'src' / project / f'{project}.csproj'),
                            '-c', 'Release', '-o', str(packages)], cwd=root, check=True)
            # Take the version from the package produced, which reflects any MSBuild overrides.
            built = list(packages.glob(f'{project}.*.nupkg'))
            if len(built) != 1:
                raise SystemExit(f'Expected one {project} package in {packages}, found {len(built)}.')
            versions[project] = built[0].name[len(project) + 1:-len('.nupkg')]
        # Install only the exact packages just built; do not race a remote feed with the same version.
        config = ET.Element('configuration')
        sources = ET.SubElement(config, 'packageSources')
        ET.SubElement(sources, 'clear')
        ET.SubElement(sources, 'add', key='local-build', value=str(packages))
        config_path = Path(temporary) / 'NuGet.Config'
        ET.ElementTree(config).write(config_path, encoding='utf-8', xml_declaration=True)
        install_env = {**os.environ, "NUGET_PACKAGES": str(Path(temporary) / "package-cache")}
        destination.mkdir(parents=True, exist_ok=False)
        try:
            for project in projects:
                # Run from the checkout so its global.json selects the same SDK that built the packages.
                subprocess.run(['dotnet', 'tool', 'install', project, '--tool-path', str(destination),
                                '--version', versions[project], '--configfile', str(config_path), '--no-http-cache'],
                               cwd=root, env=install_env, check=True)
        except BaseException:
            # This invocation created the directory; existing installations are never removed.
            # Ignore cleanup errors so they cannot hide the install failure.
            shutil.rmtree(destination, ignore_errors=True)
            raise
    print(f'\nInstalled FractalMem {versions["FractalMemory.Cli"]} from this checkout into {destination}')
    cli = destination / ('fm.exe' if os.name == 'nt' else 'fm')
    server = destination / ('fractalmem-mcp.exe' if os.name == 'nt' else 'fractalmem-mcp')
    python = 'py -3' if os.name == 'nt' else 'python3'
    print('CLI: ' + str(cli))
    print('MCP: ' + str(server))
    print(f'Try: {python} "{root / "scripts" / "demo.py"}" --cli "{cli}"')


if __name__ == '__main__':
    main()
