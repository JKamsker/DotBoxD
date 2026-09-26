"""Fail-closed license closure and CycloneDX 1.6 SBOM from restored shipping projects."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from urllib.parse import quote
import zipfile


def approved_license(metadata, identity, policy):
    license_node = metadata.find('{*}license')
    expression = (license_node.text or '').strip() if license_node is not None else ''
    if license_node is not None and license_node.get('type') == 'expression':
        if expression not in policy['allowedExpressions']:
            raise ValueError(f'{identity}: unapproved license expression {expression!r}')
        return expression
    exception = policy['exceptions'].get(identity)
    if not exception or not exception.get('reason', '').strip():
        raise ValueError(f'{identity}: missing SPDX license; add an explicitly reviewed version-specific exception')
    if exception['license'] not in policy['allowedExpressions']:
        raise ValueError(f'{identity}: exception must name an approved SPDX expression')
    return exception['license']


def component(name, version, license_expression, package_bytes):
    ref = f'pkg:nuget/{quote(name, safe="")}@{quote(version, safe="")}'
    return {
        'type': 'library', 'bom-ref': ref, 'name': name, 'version': version, 'purl': ref,
        'hashes': [{'alg': 'SHA-256', 'content': hashlib.sha256(package_bytes).hexdigest()}],
        'licenses': [{'expression': license_expression}],
    }


def inventory(root, package_directory, policy):
    solution = ET.parse(root / 'DotBoxD.Packages.slnx').getroot()
    projects = [root / node.attrib['Path'] for node in solution.iter('Project')]
    if not projects:
        raise ValueError('Shipping solution contains no projects')
    components = {}
    edges = {}
    identities = {}
    for project in projects:
        assets_path = project.parent / 'obj/project.assets.json'
        assets = json.loads(assets_path.read_text(encoding='utf-8-sig'))
        for identity, library in assets['libraries'].items():
            if library['type'] != 'package' or identity.lower() in identities:
                continue
            name, version = identity.rsplit('/', 1)
            directories = [Path(folder) / library['path'] for folder in assets['packageFolders']]
            directory = next((folder for folder in directories if folder.is_dir()), None)
            if directory is None:
                raise ValueError(f'{identity}: restored package directory missing')
            nuspecs = list(directory.glob('*.nuspec'))
            archives = list(directory.glob('*.nupkg'))
            if len(nuspecs) != 1 or len(archives) != 1:
                raise ValueError(f'{identity}: expected one restored nuspec and nupkg')
            metadata = ET.parse(nuspecs[0]).getroot().find('{*}metadata')
            item = component(name, version, approved_license(metadata, identity, policy), archives[0].read_bytes())
            identities[identity.lower()] = item['bom-ref']
            components[item['bom-ref']] = item
        for target in assets['targets'].values():
            for identity, library in target.items():
                if library['type'] != 'package':
                    continue
                source = identities[identity.lower()]
                dependencies = edges.setdefault(source, set())
                # Resolve from the selected target, not the requested NuGet version range.
                by_name = {key.rsplit('/', 1)[0].lower(): key for key, value in target.items() if value['type'] == 'package'}
                for dependency in library.get('dependencies', {}):
                    selected = by_name.get(dependency.lower())
                    if selected is None:
                        # NuGet can prune analyzer/build-only dependencies from a target.
                        # The resolved assets graph, rather than the nuspec request, is authoritative.
                        continue
                    dependencies.add(identities[selected.lower()])
    archives = sorted(package_directory.glob('*.nupkg'))
    if not archives:
        raise ValueError('No released packages found')
    for archive in archives:
        with zipfile.ZipFile(archive) as package:
            nuspecs = [name for name in package.namelist() if name.endswith('.nuspec')]
            if len(nuspecs) != 1:
                raise ValueError(f'{archive}: expected one nuspec')
            metadata = ET.fromstring(package.read(nuspecs[0])).find('{*}metadata')
        name = metadata.findtext('{*}id')
        version = metadata.findtext('{*}version')
        item = component(name, version, approved_license(metadata, f'{name}/{version}', policy), archive.read_bytes())
        components[item['bom-ref']] = item
        # Release inventory includes build/analyzer dependencies as well as runtime dependencies.
        # Explicitly label the union rather than claiming every dependency is used by every package.
        item['properties'] = [{'name': 'dotboxd:inventory-role', 'value': 'release-artifact'}]
    return {
        'bomFormat': 'CycloneDX', 'specVersion': '1.6', 'version': 1,
        'metadata': {'properties': [{'name': 'dotboxd:scope', 'value': 'shipping solution resolved runtime and build dependency union'}]},
        'components': sorted(components.values(), key=lambda item: item['bom-ref']),
        'dependencies': [{'ref': ref, 'dependsOn': sorted(dependencies)} for ref, dependencies in sorted(edges.items())],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--packages', type=Path, default=Path('artifacts/packages'))
    parser.add_argument('--output', type=Path, default=Path('artifacts/packages/sbom.cdx.json'))
    args = parser.parse_args()
    policy = json.loads((args.root / 'eng/supply-chain/policy.json').read_text())
    document = inventory(args.root, args.packages, policy)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=2) + '\n', encoding='utf-8')
    print(f'License policy passed; SBOM contains {len(document["components"])} components: {args.output}')


if __name__ == '__main__':
    main()
