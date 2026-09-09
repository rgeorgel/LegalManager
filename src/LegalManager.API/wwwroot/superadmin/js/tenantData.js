import { getToken } from './superAdminApi.js';

export async function exportarTenant(tenantId) {
  const token = getToken();
  const res = await fetch(`/api/superadmin/tenants/${tenantId}/export`, {
    method: 'GET',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!res.ok) {
    let msg = `HTTP ${res.status}`;
    try {
      const body = await res.json();
      msg = body.message || msg;
    } catch {
      try { msg = (await res.text()).substring(0, 200); } catch {}
    }
    throw new Error(msg);
  }

  const blob = await res.blob();
  const cdHeader = res.headers.get('content-disposition') || '';
  let filename = `tenant-${tenantId}.json`;
  const match = /filename\*?=(?:UTF-8'')?["']?([^;"']+)/i.exec(cdHeader);
  if (match && match[1]) filename = decodeURIComponent(match[1]);

  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.style.display = 'none';
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 60000);
  return filename;
}

export async function importarTenant(tenantId, file, options) {
  const opts = options || {};
  const mode = opts.mode || 'replace';

  const formData = new FormData();
  formData.append('file', file);
  formData.append('mode', mode);
  if (mode === 'replace' && opts.confirmation) {
    formData.append('confirmation', opts.confirmation);
  }
  if (mode === 'new' && opts.newTenantName) {
    formData.append('newTenantName', opts.newTenantName);
  }

  const token = getToken();
  const res = await fetch(`/api/superadmin/tenants/${tenantId}/import`, {
    method: 'POST',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: formData,
  });

  let body = null;
  const text = await res.text();
  try { body = text ? JSON.parse(text) : null; } catch { body = { message: text }; }

  if (!res.ok) {
    throw new Error(body?.message || `HTTP ${res.status}`);
  }
  return body;
}

export async function excluirTenant(tenantId, confirmation) {
  const token = getToken();
  const res = await fetch(`/api/superadmin/tenants/${tenantId}`, {
    method: 'DELETE',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({ confirmation }),
  });

  let body = null;
  const text = await res.text();
  try { body = text ? JSON.parse(text) : null; } catch { body = { message: text }; }

  if (!res.ok) {
    throw new Error(body?.message || `HTTP ${res.status}`);
  }
  return body;
}

export function formatarResultadoImport(result) {
  if (!result) return '';
  const linhas = [];
  const modeLabel = result.mode === 'CreateNew' ? '🆕 Tenant NOVO criado:' : '♻️ Tenant substituído:';
  linhas.push(modeLabel);
  linhas.push(`  Nome: ${result.tenantNome}`);
  linhas.push(`  ID: ${result.targetTenantId}`);
  linhas.push('');
  linhas.push(`Tabelas importadas: ${result.tablesImported}`);
  linhas.push(`Linhas inseridas: ${result.rowsImported}`);
  if (result.usersResetPasswords > 0) {
    linhas.push(`Usuários com senha resetada: ${result.usersResetPasswords}`);
  }
  if (result.warnings && result.warnings.length > 0) {
    linhas.push('');
    linhas.push('Avisos:');
    result.warnings.forEach(w => linhas.push(`• ${w}`));
  }
  return linhas.join('\n');
}
