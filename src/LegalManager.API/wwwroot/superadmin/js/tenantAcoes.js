// Modais de ação sobre um tenant no superadmin (plano/trial, status, exportar, importar,
// excluir), compartilhados entre a listagem (tenants.html) e a página de um tenant
// (tenant-detalhe.html). initTenantAcoes() injeta os modais no <body> e expõe em window as
// funções chamadas pelos botões (openPlano, openStatus, openExportModal, openImportModal,
// openDeleteModal).
import { saFetch } from '/superadmin/js/superAdminApi.js';
import { exportarTenant, importarTenant, excluirTenant, formatarResultadoImport } from '/superadmin/js/tenantData.js';

const MODAIS_HTML = `
  <!-- Modal: Alterar Plano -->
  <div class="modal-overlay" id="planoModal">
    <div class="modal">
      <div class="modal-title">Alterar Plano</div>
      <div class="form-group">
        <label class="form-label">Novo Plano</label>
        <select id="novoPlano" class="form-control">
          <option value="Free">Free</option>
          <option value="Plus">Plus</option>
          <option value="Pro">Pro</option>
          <option value="Max">Max</option>
          <option value="Enterprise">Enterprise</option>
        </select>
      </div>
      <div class="form-group">
        <label class="form-label">Período de Billing</label>
        <select id="periodoBilling" class="form-control">
          <option value="">— (não aplicável)</option>
          <option value="Mensal">Mensal</option>
          <option value="Anual">Anual</option>
        </select>
      </div>
      <div class="modal-footer">
        <button class="btn" onclick="closeModal('planoModal')">Cancelar</button>
        <button class="btn btn-primary" id="savePlanoBtn">Salvar</button>
      </div>

      <div id="trialBlock" style="display:none;margin-top:24px;padding-top:20px;border-top:1px dashed var(--color-border)">
        <div style="font-size:13px;font-weight:600;color:var(--color-text);margin-bottom:4px;display:flex;align-items:center;gap:6px">
          <span>🎁</span><span>Conceder Trial Plus</span>
        </div>
        <div style="font-size:12px;color:var(--color-text-muted);margin-bottom:12px">
          Libera o plano Plus temporariamente. Após o prazo, o tenant volta automaticamente para o plano Free.
        </div>
        <div class="form-group">
          <label class="form-label">Duração do trial</label>
          <select id="trialDias" class="form-control">
            <option value="15">15 dias</option>
            <option value="30" selected>30 dias</option>
            <option value="45">45 dias</option>
          </select>
        </div>
        <div class="form-group">
          <label class="form-label">Motivo (opcional, máx. 500 caracteres)</label>
          <textarea id="trialMotivo" class="form-control" rows="3" maxlength="500" placeholder="Ex: Migração do plano Pro, parceria, etc."></textarea>
        </div>
        <div class="modal-footer" style="margin-top:12px">
          <button class="btn btn-primary" id="saveTrialBtn">Conceder Trial Plus</button>
        </div>
      </div>
    </div>
  </div>

  <!-- Modal: Alterar Status -->
  <div class="modal-overlay" id="statusModal">
    <div class="modal">
      <div class="modal-title">Alterar Status</div>
      <div class="form-group">
        <label class="form-label">Novo Status</label>
        <select id="novoStatus" class="form-control">
          <option value="Ativo">Ativo</option>
          <option value="Trial">Trial</option>
          <option value="Suspenso">Suspenso</option>
          <option value="Cancelado">Cancelado</option>
        </select>
      </div>
      <div id="statusModalMsg" style="background:#fef3c7;border:1px solid #fde68a;color:#92400e;border-radius:6px;padding:10px 14px;font-size:13px;margin-bottom:4px;display:none">
        Suspenso/Cancelado bloqueia o acesso dos usuários do tenant.
      </div>
      <div class="modal-footer">
        <button class="btn" onclick="closeModal('statusModal')">Cancelar</button>
        <button class="btn btn-primary" id="saveStatusBtn">Confirmar</button>
      </div>
    </div>
  </div>

  <!-- Modal: Importar dados para este tenant -->
  <div class="modal-overlay" id="importModal">
    <div class="modal" style="max-width:580px">
      <div class="modal-title">Importar dados para <span id="importTenantNome"></span></div>

      <div class="form-group">
        <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer;padding:10px 12px;border:1px solid var(--color-border);border-radius:6px">
          <input type="checkbox" id="importAnonymize" checked style="margin-top:3px">
          <div>
            <div style="font-weight:500">Anonimizar dados ao importar (recomendado)</div>
            <div style="font-size:11px;color:var(--color-text-muted);margin-top:2px">
              E-mails do arquivo viram <code style="background:#f1f5f9;padding:1px 4px;border-radius:3px">*@causify-replica.com</code>
              e telefones <code style="background:#f1f5f9;padding:1px 4px;border-radius:3px">+5511900000000</code>.
              Desmarque apenas para restaurar um backup com dados reais preservados.
              Senhas de usuários são sempre resetadas para
              <code style="background:#f1f5f9;padding:1px 4px;border-radius:3px">Causify@Replic@2026!</code>, independente desta opção.
            </div>
          </div>
        </label>
        <div id="importFileHint" style="display:none;font-size:11px;margin-top:8px;padding:8px 10px;border-radius:6px;line-height:1.5"></div>
      </div>

      <div class="form-group">
        <label class="form-label">Modo de import</label>
        <div style="display:flex;flex-direction:column;gap:8px;margin-top:6px">
          <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer;padding:10px 12px;border:1px solid var(--color-border);border-radius:6px">
            <input type="radio" name="importMode" id="importModeReplace" value="replace" checked style="margin-top:3px">
            <div>
              <div style="font-weight:500">Substituir dados deste tenant</div>
              <div style="font-size:11px;color:var(--color-text-muted);margin-top:2px">
                Apaga todos os dados do tenant atual e importa o arquivo no lugar. Destrutivo.
              </div>
            </div>
          </label>
          <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer;padding:10px 12px;border:1px solid var(--color-border);border-radius:6px">
            <input type="radio" name="importMode" id="importModeNew" value="new" style="margin-top:3px">
            <div>
              <div style="font-weight:500">Criar um tenant novo a partir do arquivo</div>
              <div style="font-size:11px;color:var(--color-text-muted);margin-top:2px">
                Cria um tenant novo (plano Free, sem billing) com os dados do arquivo. Não toca no tenant atual.
              </div>
            </div>
          </label>
        </div>
      </div>

      <div class="form-group" id="importReplaceGroup">
        <div style="background:#fee2e2;border:1px solid #fecaca;color:#7f1d1d;border-radius:6px;padding:10px 14px;font-size:12px;margin-bottom:10px;line-height:1.5">
          <strong>⚠ Operação destrutiva.</strong> Todos os dados atuais deste tenant
          (contatos, processos, tarefas, documentos, financeiro, honorários,
          usuários e configurações) serão <strong>apagados permanentemente</strong>
          e substituídos pelo conteúdo do arquivo.
        </div>
        <label class="form-label">Digite o nome do tenant para confirmar</label>
        <input type="text" id="importConfirmation" class="form-control" placeholder='Digite exatamente "Nome do Escritório"'>
      </div>

      <div class="form-group" id="importNewGroup" style="display:none">
        <label class="form-label">Nome do novo tenant</label>
        <input type="text" id="importNewName" class="form-control" placeholder="Ex: Escritório X [replica]" maxlength="200">
        <div style="font-size:11px;color:var(--color-text-muted);margin-top:4px">
          Será criado com plano Free, status Ativo e sem dados de billing.
        </div>
      </div>

      <div class="form-group">
        <label class="form-label">Arquivo JSON exportado</label>
        <input type="file" id="importFile" accept=".json,application/json" class="form-control">
        <div style="font-size:11px;color:var(--color-text-muted);margin-top:4px">
          Apenas arquivos gerados pelo botão "Exportar" deste painel.
        </div>
      </div>

      <div id="importMsg" style="display:none;border-radius:6px;padding:10px 14px;font-size:13px;margin-bottom:4px;line-height:1.5"></div>

      <div class="modal-footer">
        <button class="btn" onclick="closeImportModal()">Cancelar</button>
        <button class="btn btn-danger" id="importConfirmBtn" style="background:#dc2626;color:white;border-color:#dc2626">Importar</button>
      </div>
    </div>
  </div>

  <!-- Modal: Exportar dados deste tenant -->
  <div class="modal-overlay" id="exportModal">
    <div class="modal" style="max-width:560px">
      <div class="modal-title">Exportar dados de <span id="exportTenantNome"></span></div>

      <div style="color:var(--color-text-muted);font-size:12px;margin-bottom:14px;line-height:1.5">
        Gera um arquivo JSON com todos os dados deste tenant (contatos, processos,
        tarefas, documentos, financeiro, honorários, usuários e configurações).
      </div>

      <div class="form-group">
        <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer;padding:10px 12px;border:1px solid var(--color-border);border-radius:6px">
          <input type="checkbox" id="exportKeepPersonalData" style="margin-top:3px">
          <div>
            <div style="font-weight:500">Manter e-mails e telefones reais (backup)</div>
            <div style="font-size:11px;color:var(--color-text-muted);margin-top:2px">
              Por padrão, e-mails viram <code style="background:#f1f5f9;padding:1px 4px;border-radius:3px">*@causify-replica.com</code>
              e telefones <code style="background:#f1f5f9;padding:1px 4px;border-radius:3px">+5511900000000</code>.
              Marque esta opção para manter os dados reais de contatos e usuários —
              use apenas para backup interno, pois o arquivo passa a conter dados
              pessoais reais.
            </div>
          </div>
        </label>
      </div>

      <div id="exportRealDataWarning" style="display:none;background:#fef3c7;border:1px solid #fde68a;color:#92400e;border-radius:6px;padding:10px 14px;font-size:12px;margin-bottom:4px;line-height:1.5">
        ⚠ O arquivo gerado terá dados pessoais reais (e-mails e telefones). Guarde-o
        em local seguro e não o compartilhe.
      </div>

      <div class="modal-footer">
        <button class="btn" onclick="closeExportModal()">Cancelar</button>
        <button class="btn btn-primary" id="exportConfirmBtn">⬇ Exportar JSON</button>
      </div>
    </div>
  </div>

  <!-- Modal: Excluir tenant -->
  <div class="modal-overlay" id="deleteModal">
    <div class="modal" style="max-width:520px">
      <div class="modal-title">Excluir <span id="deleteTenantNome"></span></div>

      <div style="background:#fee2e2;border:2px solid #dc2626;color:#7f1d1d;border-radius:6px;padding:12px 14px;font-size:13px;margin-bottom:14px;line-height:1.6">
        <strong>⚠ Esta ação é PERMANENTE e IRREVERSÍVEL.</strong>
        <div style="margin-top:6px">
          Todo o conteúdo deste tenant será apagado para sempre: contatos, processos,
          andamentos, tarefas, documentos, financeiro, contratos e parcelas de honorário,
          usuários e configurações. Não existe backup automático nem forma de desfazer
          esta operação depois de confirmada.
        </div>
        <div style="margin-top:6px">
          Se precisar apenas de uma cópia dos dados antes de excluir, use
          <strong>Exportar</strong> primeiro.
        </div>
      </div>

      <div class="form-group">
        <label class="form-label">Digite o nome do tenant para confirmar a exclusão</label>
        <input type="text" id="deleteConfirmation" class="form-control" placeholder='Digite exatamente "Nome do Escritório"'>
      </div>

      <div id="deleteMsg" style="display:none;border-radius:6px;padding:10px 14px;font-size:13px;margin-bottom:4px;line-height:1.5"></div>

      <div class="modal-footer">
        <button class="btn" onclick="closeDeleteModal()">Cancelar</button>
        <button class="btn btn-danger" id="deleteConfirmBtn" style="background:#dc2626;color:white;border-color:#dc2626">Excluir permanentemente</button>
      </div>
    </div>
  </div>
`;

export function showToast(message) {
  const toast = document.createElement('div');
  toast.textContent = message;
  toast.style.cssText = 'position:fixed;bottom:24px;right:24px;background:#15803d;color:white;padding:12px 18px;border-radius:8px;font-size:14px;z-index:2000;box-shadow:0 4px 12px rgba(0,0,0,.15);max-width:380px';
  document.body.appendChild(toast);
  setTimeout(() => { toast.remove(); }, 4000);
}

/**
 * @param {{ onAlterado?: () => void, onExcluido?: (nome: string) => void }} opcoes
 *   onAlterado: depois de mudar plano, trial, status ou importar dados.
 *   onExcluido: depois de excluir o tenant.
 */
export function initTenantAcoes({ onAlterado = () => {}, onExcluido = () => {} } = {}) {
  document.body.insertAdjacentHTML('beforeend', MODAIS_HTML);
  let currentTenantId = null;

  let exportTenantId = null;

  window.openExportModal = function(id, nome) {
    exportTenantId = id;
    document.getElementById('exportTenantNome').textContent = nome;
    document.getElementById('exportKeepPersonalData').checked = false;
    document.getElementById('exportRealDataWarning').style.display = 'none';
    openModal('exportModal');
  };

  window.closeExportModal = function() {
    closeModal('exportModal');
    exportTenantId = null;
  };

  document.getElementById('exportKeepPersonalData').addEventListener('change', (e) => {
    document.getElementById('exportRealDataWarning').style.display = e.target.checked ? 'block' : 'none';
  });

  document.getElementById('exportConfirmBtn').addEventListener('click', async () => {
    const btn = document.getElementById('exportConfirmBtn');
    const keepPersonalData = document.getElementById('exportKeepPersonalData').checked;
    const original = btn.textContent;
    try {
      btn.disabled = true;
      btn.textContent = 'Baixando...';
      const filename = await exportarTenant(exportTenantId, { anonymize: !keepPersonalData });
      showToast(`Exportação concluída: ${filename}`);
      closeExportModal();
    } catch (e) {
      alert('Erro ao exportar: ' + e.message);
    } finally {
      btn.disabled = false;
      btn.textContent = original;
    }
  });



  window.openPlano = function(id, planoAtual, periodoAtual, statusAtual) {
    currentTenantId = id;
    document.getElementById('novoPlano').value = planoAtual;
    document.getElementById('periodoBilling').value = periodoAtual || '';
    const trialBlock = document.getElementById('trialBlock');
    const isFreeActive = planoAtual === 'Free' && statusAtual === 'Ativo';
    trialBlock.style.display = isFreeActive ? '' : 'none';
    if (isFreeActive) {
      document.getElementById('trialDias').value = '30';
      document.getElementById('trialMotivo').value = '';
    }
    openModal('planoModal');
  };

  document.getElementById('savePlanoBtn').addEventListener('click', async () => {
    const btn = document.getElementById('savePlanoBtn');
    btn.disabled = true;
    try {
      await saFetch(`/superadmin/tenants/${currentTenantId}/plano`, {
        method: 'PUT',
        body: JSON.stringify({
          plano: document.getElementById('novoPlano').value,
          periodoBilling: document.getElementById('periodoBilling').value || null
        })
      });
      closeModal('planoModal');
      onAlterado();
    } catch (e) {
      alert('Erro: ' + e.message);
    } finally {
      btn.disabled = false;
    }
  });

  document.getElementById('saveTrialBtn').addEventListener('click', async () => {
    const btn = document.getElementById('saveTrialBtn');
    const dias = parseInt(document.getElementById('trialDias').value, 10);
    const motivo = document.getElementById('trialMotivo').value.trim() || null;
    if (!confirm(`Conceder Trial Plus de ${dias} dias para este tenant? O plano voltará automaticamente para Free após o prazo.`)) return;
    btn.disabled = true;
    try {
      await saFetch(`/superadmin/tenants/${currentTenantId}/trial-plus`, {
        method: 'PUT',
        body: JSON.stringify({ dias, motivo })
      });
      closeModal('planoModal');
      onAlterado();
    } catch (e) {
      alert('Erro: ' + e.message);
    } finally {
      btn.disabled = false;
    }
  });

  window.openStatus = function(id, statusAtual) {
    currentTenantId = id;
    document.getElementById('novoStatus').value = statusAtual;
    document.getElementById('statusModalMsg').style.display = 'none';
    openModal('statusModal');
  };

  let importTenantId = null;
  let importTenantNome = null;

  function getImportMode() {
    const replaceRadio = document.getElementById('importModeReplace');
    return replaceRadio && replaceRadio.checked ? 'replace' : 'new';
  }

  function syncImportModeUi() {
    const mode = getImportMode();
    const replaceGroup = document.getElementById('importReplaceGroup');
    const newGroup = document.getElementById('importNewGroup');
    const btn = document.getElementById('importConfirmBtn');
    if (mode === 'replace') {
      replaceGroup.style.display = '';
      newGroup.style.display = 'none';
      btn.textContent = 'Importar e substituir';
    } else {
      replaceGroup.style.display = 'none';
      newGroup.style.display = '';
      btn.textContent = 'Criar tenant e importar';
    }
  }

  window.openImportModal = function(id, nome) {
    importTenantId = id;
    importTenantNome = nome;
    document.getElementById('importTenantNome').textContent = nome;
    document.getElementById('importFile').value = '';
    document.getElementById('importConfirmation').value = '';
    document.getElementById('importNewName').value = '';
    document.getElementById('importAnonymize').checked = true;
    const hint = document.getElementById('importFileHint');
    hint.style.display = 'none';
    hint.textContent = '';
    const msg = document.getElementById('importMsg');
    msg.style.display = 'none';
    msg.textContent = '';
    document.getElementById('importModeReplace').checked = true;
    syncImportModeUi();
    openModal('importModal');
  };

  // Espia só o início do arquivo (o bloco "anonymization" vem antes da lista de
  // tabelas no JSON gerado pelo export) para avisar se o backup contém dados reais,
  // sem precisar carregar o arquivo inteiro na memória.
  document.getElementById('importFile').addEventListener('change', async (e) => {
    const hint = document.getElementById('importFileHint');
    hint.style.display = 'none';
    hint.textContent = '';

    const file = e.target.files && e.target.files[0];
    if (!file) return;

    try {
      const head = await file.slice(0, 8192).text();
      const match = /"anonymization"\s*:\s*\{\s*"enabled"\s*:\s*(true|false)/.exec(head);
      if (!match) return;

      if (match[1] === 'false') {
        hint.style.background = '#fef3c7';
        hint.style.border = '1px solid #fde68a';
        hint.style.color = '#92400e';
        hint.textContent = '⚠ Este arquivo contém dados reais (e-mails/telefones não anonimizados). Desmarque "Anonimizar dados ao importar" acima para preservá-los.';
      } else {
        hint.style.background = '#eff6ff';
        hint.style.border = '1px solid #bfdbfe';
        hint.style.color = '#1e3a8a';
        hint.textContent = '✓ Este arquivo já está anonimizado.';
      }
      hint.style.display = 'block';
    } catch {
      // Leitura parcial falhou (arquivo não é o esperado) — deixa a validação normal
      // do backend lidar com isso ao importar.
    }
  });

  window.closeImportModal = function() {
    closeModal('importModal');
    importTenantId = null;
    importTenantNome = null;
  };

  document.getElementById('importConfirmBtn').addEventListener('click', async () => {
    const btn = document.getElementById('importConfirmBtn');
    const fileInput = document.getElementById('importFile');
    const confirmInput = document.getElementById('importConfirmation');
    const newNameInput = document.getElementById('importNewName');
    const msg = document.getElementById('importMsg');

    msg.style.display = 'none';
    msg.textContent = '';

    if (!fileInput.files || fileInput.files.length === 0) {
      msg.style.background = '#fef3c7';
      msg.style.border = '1px solid #fde68a';
      msg.style.color = '#92400e';
      msg.textContent = 'Selecione um arquivo JSON.';
      msg.style.display = 'block';
      return;
    }

    const file = fileInput.files[0];
    if (file.size > 500 * 1024 * 1024) {
      msg.style.background = '#fee2e2';
      msg.style.border = '1px solid #fecaca';
      msg.style.color = '#7f1d1d';
      msg.textContent = 'Arquivo maior que 500 MB.';
      msg.style.display = 'block';
      return;
    }

    const mode = getImportMode();
    let confirmation = null;
    let newTenantName = null;

    if (mode === 'replace') {
      const typed = confirmInput.value.trim();
      if (typed !== importTenantNome) {
        msg.style.background = '#fef3c7';
        msg.style.border = '1px solid #fde68a';
        msg.style.color = '#92400e';
        msg.textContent = `Confirmação inválida. Digite exatamente "${importTenantNome}".`;
        msg.style.display = 'block';
        return;
      }
      confirmation = typed;
    } else {
      const name = newNameInput.value.trim();
      if (!name) {
        msg.style.background = '#fef3c7';
        msg.style.border = '1px solid #fde68a';
        msg.style.color = '#92400e';
        msg.textContent = 'Informe o nome do novo tenant.';
        msg.style.display = 'block';
        return;
      }
      if (name.length > 200) {
        msg.style.background = '#fef3c7';
        msg.style.border = '1px solid #fde68a';
        msg.style.color = '#92400e';
        msg.textContent = 'Nome do tenant deve ter no máximo 200 caracteres.';
        msg.style.display = 'block';
        return;
      }
      newTenantName = name;
    }

    btn.disabled = true;
    const originalText = btn.textContent;
    btn.textContent = 'Importando...';

    try {
      const result = await importarTenant(importTenantId, file, {
        mode,
        confirmation,
        newTenantName,
        anonymize: document.getElementById('importAnonymize').checked
      });
      msg.style.background = '#dcfce7';
      msg.style.border = '1px solid #bbf7d0';
      msg.style.color = '#15803d';
      msg.style.whiteSpace = 'pre-line';
      msg.textContent = 'Importação concluída com sucesso!\n\n' + formatarResultadoImport(result);
      msg.style.display = 'block';
      onAlterado();
    } catch (e) {
      msg.style.background = '#fee2e2';
      msg.style.border = '1px solid #fecaca';
      msg.style.color = '#7f1d1d';
      msg.style.whiteSpace = 'pre-line';
      msg.textContent = 'Erro: ' + e.message;
      msg.style.display = 'block';
    } finally {
      btn.disabled = false;
      btn.textContent = originalText;
    }
  });

  let deleteTenantId = null;
  let deleteTenantNome = null;

  window.openDeleteModal = function(id, nome) {
    deleteTenantId = id;
    deleteTenantNome = nome;
    document.getElementById('deleteTenantNome').textContent = nome;
    document.getElementById('deleteConfirmation').value = '';
    const msg = document.getElementById('deleteMsg');
    msg.style.display = 'none';
    msg.textContent = '';
    openModal('deleteModal');
  };

  window.closeDeleteModal = function() {
    closeModal('deleteModal');
    deleteTenantId = null;
    deleteTenantNome = null;
  };

  document.getElementById('deleteConfirmBtn').addEventListener('click', async () => {
    const btn = document.getElementById('deleteConfirmBtn');
    const confirmInput = document.getElementById('deleteConfirmation');
    const msg = document.getElementById('deleteMsg');

    msg.style.display = 'none';
    msg.textContent = '';

    const typed = confirmInput.value.trim();
    if (typed !== deleteTenantNome) {
      msg.style.background = '#fef3c7';
      msg.style.border = '1px solid #fde68a';
      msg.style.color = '#92400e';
      msg.textContent = `Confirmação inválida. Digite exatamente "${deleteTenantNome}".`;
      msg.style.display = 'block';
      return;
    }

    btn.disabled = true;
    const originalText = btn.textContent;
    btn.textContent = 'Excluindo...';

    try {
      await excluirTenant(deleteTenantId, typed);
      const nomeExcluido = deleteTenantNome;
      closeDeleteModal();
      onExcluido(nomeExcluido);
    } catch (e) {
      msg.style.background = '#fee2e2';
      msg.style.border = '1px solid #fecaca';
      msg.style.color = '#7f1d1d';
      msg.textContent = 'Erro: ' + e.message;
      msg.style.display = 'block';
    } finally {
      btn.disabled = false;
      btn.textContent = originalText;
    }
  });


  document.getElementById('novoStatus').addEventListener('change', e => {
    const msg = document.getElementById('statusModalMsg');
    msg.style.display = (e.target.value === 'Suspenso' || e.target.value === 'Cancelado') ? '' : 'none';
  });

  document.querySelectorAll('input[name="importMode"]').forEach(r => {
    r.addEventListener('change', syncImportModeUi);
  });

  document.getElementById('saveStatusBtn').addEventListener('click', async () => {
    const btn = document.getElementById('saveStatusBtn');
    btn.disabled = true;
    try {
      await saFetch(`/superadmin/tenants/${currentTenantId}/status`, {
        method: 'PUT',
        body: JSON.stringify({ status: document.getElementById('novoStatus').value })
      });
      closeModal('statusModal');
      onAlterado();
    } catch (e) {
      alert('Erro: ' + e.message);
    } finally {
      btn.disabled = false;
    }
  });

  function openModal(id) { document.getElementById(id).classList.add('open'); }
  window.closeModal = function(id) { document.getElementById(id).classList.remove('open'); };

  document.querySelectorAll('.modal-overlay').forEach(m => {
    m.addEventListener('click', e => { if (e.target === m) m.classList.remove('open'); });
  });
}
