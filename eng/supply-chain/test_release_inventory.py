import unittest
import xml.etree.ElementTree as ET
from release_inventory import approved_license, component


class LicensePolicyTests(unittest.TestCase):
    def setUp(self):
        self.policy = {'allowedExpressions': ['MIT'], 'exceptions': {}}

    def test_approved_expression(self):
        metadata = ET.fromstring('<metadata><license type="expression">MIT</license></metadata>')
        self.assertEqual('MIT', approved_license(metadata, 'Package/1.0', self.policy))

    def test_unapproved_and_missing_license_fail_closed(self):
        for xml in ['<metadata/>', '<metadata><license type="expression">AGPL-3.0-only</license></metadata>']:
            with self.assertRaises(ValueError):
                approved_license(ET.fromstring(xml), 'Package/1.0', self.policy)

    def test_exception_requires_exact_version_and_reason(self):
        metadata = ET.fromstring('<metadata><licenseUrl>https://example.com/license</licenseUrl></metadata>')
        self.policy['exceptions']['Package/1.0'] = {'license': 'MIT', 'reason': 'Reviewed upstream LICENSE'}
        self.assertEqual('MIT', approved_license(metadata, 'Package/1.0', self.policy))
        with self.assertRaises(ValueError):
            approved_license(metadata, 'Package/2.0', self.policy)
        self.policy['exceptions']['Package/1.0']['reason'] = ''
        with self.assertRaises(ValueError):
            approved_license(metadata, 'Package/1.0', self.policy)

    def test_sbom_hash_covers_exact_package_bytes(self):
        first = component('Package', '1.0', 'MIT', b'first')
        second = component('Package', '1.0', 'MIT', b'second')
        self.assertNotEqual(first['hashes'], second['hashes'])
        self.assertEqual('pkg:nuget/Package@1.0', first['purl'])


if __name__ == '__main__':
    unittest.main()
