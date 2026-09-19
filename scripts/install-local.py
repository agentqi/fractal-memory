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
        parser.error(f'Tool directory already exists: {destination}. Choose a new --tool-dir.')
    if not shutil.which('dotnet'):
        parser.error('Install the .NET 10 SDK first: https://dotnet.microsoft.com/download/dotnet/10.0')
    version = ET.parse(root / 'Directory.Build.props').findtext('.//VersionPrefix')
    if not version:
        parser.error('VersionPrefix is missing from Directory.Build.props.')
    with tempfile.TemporaryDirectory(prefix='fractalmem-install-') as temporary:
        packages = Path(temporary) / 'packages'
        for project in ['FractalMemory.Cli', 'FractalMemory.McpServer']:
            subprocess.run(['dotnet', 'pack', str(root / 'src' / project / f'{project}.csproj'),
                            '-c', 'Release', '-o', str(packages)], cwd=root, check=True)
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
            for package in ['FractalMemory.Cli', 'FractalMemory.McpServer']:
                subprocess.run(['dotnet', 'tool', 'install', package, '--tool-path', str(destination),
                                '--version', version, '--configfile', str(config_path), '--no-http-cache'],
                               env=install_env, check=True)
        except BaseException:
            # This invocation created the directory; existing installations are never removed.
            shutil.rmtree(destination)
            raise
    print(f'\nInstalled FractalMem {version} from this checkout into {destination}')
    cli = destination / ('fm.exe' if os.name == 'nt' else 'fm')
    server = destination / ('fractalmem-mcp.exe' if os.name == 'nt' else 'fractalmem-mcp')
    print('CLI: ' + str(cli))
    print('MCP: ' + str(server))
    print('Try: python scripts/demo.py --cli "' + str(cli) + '"')


if __name__ == '__main__':
    main()
