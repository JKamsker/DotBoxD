const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { discoveryDirectory, readDescriptors } = require('../discovery');

test('uses protocol casing for contributed pause scopes', () => {
  const manifest = require('../package.json');
  const pauseScope = manifest.contributes.debuggers[0]
    .configurationAttributes.attach.properties.pauseScope;

  assert.deepEqual(pauseScope.enum, ['server', 'pluginSession', 'execution']);
});

test('uses the same per-user Linux discovery location as PluginDebugBridge', () => {
  assert.equal(
    discoveryDirectory({ environment: {}, home: '/home/plugin', platform: 'linux' }),
    path.join('/home/plugin', '.local', 'share', 'DotBoxD', 'Debug'));
});

test('reads valid descriptors and ignores partially written or removed files', async () => {
  const directory = await fs.promises.mkdtemp(path.join(os.tmpdir(), 'dotboxd-discovery-'));
  try {
    await fs.promises.writeFile(path.join(directory, 'broken.json'), '{');
    await fs.promises.writeFile(path.join(directory, '42.json'), JSON.stringify({
      ProcessId: 42,
      PipeName: 'dotboxd-debug-test',
      DiscoveryToken: 'secret'
    }));

    const descriptors = await readDescriptors(directory);

    assert.equal(descriptors.length, 1);
    assert.equal(descriptors[0].ProcessId, 42);
  } finally {
    await fs.promises.rm(directory, { recursive: true, force: true });
  }
});
