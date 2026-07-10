const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

function localApplicationData(environment = process.env, home = os.homedir(), platform = process.platform) {
  if (platform === 'win32') {
    return environment.LOCALAPPDATA || path.join(home, 'AppData', 'Local');
  }

  if (platform === 'darwin') {
    return path.join(home, 'Library', 'Application Support');
  }

  return environment.XDG_DATA_HOME || path.join(home, '.local', 'share');
}

function discoveryDirectory(options = {}) {
  return path.join(
    localApplicationData(options.environment, options.home, options.platform),
    'DotBoxD',
    'Debug');
}

async function readDescriptors(directory = discoveryDirectory()) {
  let names;
  try {
    names = await fs.promises.readdir(directory);
  } catch (error) {
    if (error.code === 'ENOENT') {
      return [];
    }

    throw error;
  }

  const descriptors = [];
  for (const name of names.filter(item => item.endsWith('.json'))) {
    try {
      const descriptor = JSON.parse(await fs.promises.readFile(path.join(directory, name), 'utf8'));
      if (Number.isInteger(descriptor.ProcessId) && descriptor.ProcessId > 0 && descriptor.PipeName) {
        descriptors.push(descriptor);
      }
    } catch (error) {
      if (error.code !== 'ENOENT' && !(error instanceof SyntaxError)) {
        throw error;
      }
    }
  }

  return descriptors.sort((left, right) => left.ProcessId - right.ProcessId);
}

module.exports = { discoveryDirectory, localApplicationData, readDescriptors };
