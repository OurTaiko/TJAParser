// Starts tja-language-server (dotnet~/OurTaiko.TjaParser.LanguageServer) for .tja files.
const { workspace } = require('vscode');
const { LanguageClient } = require('vscode-languageclient/node');

let client;

exports.activate = async function activate() {
  const path = workspace.getConfiguration('tja').get('server.path') || 'tja-language-server';
  const run = path.endsWith('.dll') ? { command: 'dotnet', args: [path] } : { command: path };
  client = new LanguageClient('tja', 'TJA', { run, debug: run }, { documentSelector: [{ language: 'tja' }] });
  await client.start();
};

exports.deactivate = function deactivate() {
  return client ? client.stop() : undefined;
};
