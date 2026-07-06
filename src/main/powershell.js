'use strict';

const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const SCRIPTS_DIR = path.join(__dirname, 'scripts');

/**
 * Executa um script PowerShell (.ps1) da pasta scripts/ e devolve o JSON
 * que o script imprime no stdout.
 *
 * Os scripts sao lidos em memoria e enviados via -EncodedCommand, o que
 * funciona tanto em desenvolvimento quanto empacotado (inclusive dentro do
 * asar), sem depender de executar o arquivo direto do disco.
 *
 * @param {string} scriptName  nome do arquivo, ex: "clean-temp.ps1"
 * @param {Object} params      pares chave/valor expostos ao script como
 *                             variaveis de ambiente FB_<CHAVE>
 * @returns {Promise<Object>}
 */
function runScript(scriptName, params = {}) {
  return new Promise((resolve, reject) => {
    const scriptPath = path.join(SCRIPTS_DIR, scriptName);

    let body;
    try {
      body = fs.readFileSync(scriptPath, 'utf8');
    } catch (err) {
      return reject(new Error(`Script nao encontrado: ${scriptName}`));
    }

    // Forca o stdout do PowerShell em UTF-8 (sem BOM) para que acentos
    // sobrevivam ao pipe ate o Node, que decodifica como UTF-8.
    const prefix =
      '[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false;\r\n';
    const encoded = Buffer.from(prefix + body, 'utf16le').toString('base64');

    const env = { ...process.env };
    for (const [key, value] of Object.entries(params)) {
      env['FB_' + key.toUpperCase()] =
        typeof value === 'string' ? value : JSON.stringify(value);
    }

    const ps = spawn(
      'powershell.exe',
      [
        '-NoProfile',
        '-NonInteractive',
        '-ExecutionPolicy',
        'Bypass',
        '-EncodedCommand',
        encoded,
      ],
      { env, windowsHide: true }
    );

    let stdout = '';
    let stderr = '';

    ps.stdout.on('data', (chunk) => (stdout += chunk.toString()));
    ps.stderr.on('data', (chunk) => (stderr += chunk.toString()));
    ps.on('error', (err) => reject(err));

    ps.on('close', (code) => {
      const text = stdout.replace(/^﻿/, '').trim();
      if (text) {
        try {
          return resolve(JSON.parse(text));
        } catch (_) {
          return resolve({ ok: code === 0, raw: text, stderr: stderr.trim() });
        }
      }
      if (code === 0) return resolve({ ok: true });
      reject(new Error(stderr.trim() || `PowerShell saiu com codigo ${code}`));
    });
  });
}

module.exports = { runScript, SCRIPTS_DIR };
