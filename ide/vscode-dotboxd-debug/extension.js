const fs = require('node:fs');
const path = require('node:path');
const vscode = require('vscode');
const { readDescriptors } = require('./discovery');

function adapterPath(context) {
  const configured = vscode.workspace.getConfiguration('dotboxd.kernelDebug').get('adapterPath');
  const candidates = [
    configured,
    context.asAbsolutePath(path.join('adapter', 'DotBoxD.DebugAdapter.dll')),
    ...(vscode.workspace.workspaceFolders || []).flatMap(folder => [
      path.join(folder.uri.fsPath, 'tools', 'DotBoxD.DebugAdapter', 'bin', 'Debug', 'net10.0', 'DotBoxD.DebugAdapter.dll'),
      path.join(folder.uri.fsPath, 'tools', 'DotBoxD.DebugAdapter', 'bin', 'Release', 'net10.0', 'DotBoxD.DebugAdapter.dll')
    ])
  ].filter(Boolean);
  const found = candidates.find(candidate => path.isAbsolute(candidate) && fs.existsSync(candidate));
  if (!found) {
    throw new Error('DotBoxD.DebugAdapter.dll was not found. Build the adapter or configure dotboxd.kernelDebug.adapterPath.');
  }

  return found;
}

async function pickKernelProcess() {
  const deadline = Date.now() + 30000;
  let descriptors = [];
  while (descriptors.length === 0 && Date.now() < deadline) {
    descriptors = await readDescriptors();
    if (descriptors.length === 0) {
      await new Promise(resolve => setTimeout(resolve, 100));
    }
  }

  if (descriptors.length === 0) {
    throw new Error('No plugin process has an active DotBoxD kernel debug bridge. Launch it with DOTBOXD_KERNEL_DEBUG=1.');
  }

  const selected = await vscode.window.showQuickPick(
    descriptors.map(descriptor => ({
      label: `PID ${descriptor.ProcessId}`,
      description: descriptor.PipeName,
      processId: descriptor.ProcessId
    })),
    { placeHolder: 'Select the plugin process that owns the kernel source maps' });
  return selected ? String(selected.processId) : undefined;
}

function activate(context) {
  context.subscriptions.push(
    vscode.commands.registerCommand('dotboxd.pickKernelProcess', pickKernelProcess),
    vscode.debug.registerDebugAdapterDescriptorFactory('dotboxd-kernel', {
      createDebugAdapterDescriptor() {
        return new vscode.DebugAdapterExecutable('dotnet', [adapterPath(context)]);
      }
    }));
}

function deactivate() {}

module.exports = { activate, deactivate };
