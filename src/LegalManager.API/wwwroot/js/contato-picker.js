import { esc } from './utils.js';
import { getContatos, createContato } from './contatos.js';
import { mascaraCpfCnpj, mascaraTelefone } from './mascaras.js';
import { createSearchPicker } from './search-picker.js';

const TIPO_CONTATO = {
  Cliente: 'Cliente', ParteContraria: 'Parte contrária', Testemunha: 'Testemunha', Perito: 'Perito', Outro: 'Outro',
};

/**
 * Campo de busca de contato (autocomplete no servidor), no lugar de um <select> com todos os
 * contatos — que fica inviável com muitos cadastros e só trazia a primeira página.
 *
 *   const picker = createContatoPicker(document.getElementById('pContatoPicker'), {
 *     valueClass: 'parte-contato',            // classe do <input hidden> que guarda o id
 *     value: { id, label },                   // seleção inicial (opcional)
 *     onChange: (contato) => { ... },         // { id, label, ... } ou null
 *   });
 *   picker.getValue(); picker.getLabel(); picker.setValue({ id, label }); picker.focus();
 *
 * Busca por nome, CPF/CNPJ ou e-mail (mesmo filtro da tela de contatos), só contatos ativos.
 * Digitar não apaga a seleção: ao sair do campo sem escolher outro, o nome selecionado volta.
 * O fim da lista tem "+ Cadastrar novo contato": abre um cadastro rápido (sem sair da tela) e
 * já seleciona o contato criado. `tipoContatoPadrao` é a categoria sugerida nesse cadastro
 * (valor ou função avaliada no clique — ex.: depende do tipo de parte escolhido na linha).
 */
export function createContatoPicker(root, {
  valueClass = '', value = null, placeholder, onChange, tipoContatoPadrao = 'Cliente',
} = {}) {
  return createSearchPicker(root, {
    valueClass, value, onChange,
    placeholder: placeholder || 'Digite nome, CPF/CNPJ ou e-mail...',
    buscar: async (termo) =>
      (await getContatos({ busca: termo, ativo: true, sortBy: 'nome', sortDir: 'asc', pageSize: 20 }))?.items,
    opcao: (c) => ({
      id: c.id,
      label: c.nome,
      detalhe: [TIPO_CONTATO[c.tipoContato] || c.tipoContato, c.cpfCnpj, c.email].filter(Boolean).map(esc).join(' · '),
    }),
    vazio: (termo) => `Nenhum contato encontrado${termo ? ` para "${esc(termo)}"` : ''}.`,
    criar: {
      rotulo: (termo) => termo ? `+ Cadastrar "${esc(termo)}" como novo contato` : '+ Cadastrar novo contato',
      acao: (termo) => cadastrarContatoRapido({
        nome: termo,
        tipoContato: typeof tipoContatoPadrao === 'function' ? tipoContatoPadrao() : tipoContatoPadrao,
      }),
    },
    dica: 'Digite para buscar entre todos os contatos.',
    erro: 'Erro ao buscar contatos. Tente novamente.',
  });
}

// ── Cadastro rápido ───────────────────────────────────────────────────────

let modalRapido = null;

function criarModalRapido() {
  const overlay = document.createElement('div');
  overlay.className = 'modal-overlay';
  overlay.innerHTML = `
    <div class="modal" role="dialog" aria-modal="true" aria-labelledby="crTitulo">
      <div class="modal-header">
        <span class="modal-title" id="crTitulo">Novo contato</span>
        <button type="button" class="modal-close" data-cr-fechar aria-label="Fechar">✕</button>
      </div>
      <form class="modal-body" novalidate>
        <div class="alert alert-error" data-cr-alerta style="display:none"></div>
        <div class="form-row">
          <div class="form-group">
            <label for="crTipo">Tipo de Pessoa</label>
            <select id="crTipo" class="form-control">
              <option value="PF">Pessoa Física</option>
              <option value="PJ">Pessoa Jurídica</option>
            </select>
          </div>
          <div class="form-group">
            <label for="crTipoContato">Categoria</label>
            <select id="crTipoContato" class="form-control">
              ${Object.entries(TIPO_CONTATO).map(([v, l]) => `<option value="${v}">${l}</option>`).join('')}
            </select>
          </div>
        </div>
        <div class="form-group">
          <label for="crNome">Nome *</label>
          <input type="text" id="crNome" class="form-control" maxlength="300" autocomplete="off">
        </div>
        <div class="form-row">
          <div class="form-group">
            <label for="crCpfCnpj">CPF / CNPJ</label>
            <input type="text" id="crCpfCnpj" class="form-control" placeholder="000.000.000-00" inputmode="numeric">
          </div>
          <div class="form-group">
            <label for="crTelefone">Telefone</label>
            <input type="tel" id="crTelefone" class="form-control" placeholder="(00) 00000-0000">
          </div>
        </div>
        <div class="form-group">
          <label for="crEmail">E-mail</label>
          <input type="email" id="crEmail" class="form-control">
        </div>
        <p style="font-size:12px;color:var(--color-text-muted);margin:0">
          Endereço, OAB, tags e demais dados podem ser completados depois na ficha do contato.
        </p>
        <button type="submit" hidden></button>
      </form>
      <div class="modal-footer">
        <button type="button" class="btn btn-secondary" data-cr-fechar>Cancelar</button>
        <button type="button" class="btn btn-primary" data-cr-salvar>Salvar contato</button>
      </div>
    </div>`;
  document.body.appendChild(overlay);

  const $ = (sel) => overlay.querySelector(sel);
  $('#crCpfCnpj').addEventListener('input', (e) => mascaraCpfCnpj(e.target));
  $('#crTelefone').addEventListener('input', (e) => mascaraTelefone(e.target));

  let resolver = null;
  const fechar = (contato) => {
    overlay.classList.remove('open');
    resolver?.(contato);
    resolver = null;
  };

  async function salvar() {
    const alerta = $('[data-cr-alerta]');
    const nome = $('#crNome').value.trim();
    alerta.style.display = 'none';
    if (!nome) {
      alerta.textContent = 'Informe o nome do contato.';
      alerta.style.display = 'block';
      $('#crNome').focus();
      return;
    }
    const btn = $('[data-cr-salvar]');
    btn.disabled = true;
    btn.innerHTML = '<span class="spinner"></span>';
    try {
      const contato = await createContato({
        tipo: $('#crTipo').value,
        tipoContato: $('#crTipoContato').value,
        nome,
        cpfCnpj: $('#crCpfCnpj').value.trim() || null,
        email: $('#crEmail').value.trim() || null,
        telefone: $('#crTelefone').value.trim() || null,
        notificacaoHabilitada: false,
        tags: [],
      });
      fechar(contato);
    } catch (err) {
      alerta.textContent = err.message;
      alerta.style.display = 'block';
    } finally {
      btn.disabled = false;
      btn.textContent = 'Salvar contato';
    }
  }

  overlay.querySelectorAll('[data-cr-fechar]').forEach(b => b.addEventListener('click', () => fechar(null)));
  $('[data-cr-salvar]').addEventListener('click', salvar);
  $('form').addEventListener('submit', (e) => { e.preventDefault(); salvar(); });
  // Esc fecha só este modal, não o formulário que está por baixo.
  overlay.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); fechar(null); }
  });

  return {
    abrir({ nome = '', tipoContato = 'Cliente' }) {
      overlay.querySelector('form').reset();
      $('[data-cr-alerta]').style.display = 'none';
      $('#crTipoContato').value = TIPO_CONTATO[tipoContato] ? tipoContato : 'Cliente';
      // Se o termo buscado parece um CPF/CNPJ, vai para o campo certo em vez do nome.
      const digitos = nome.replace(/\D/g, '');
      if ((digitos.length === 11 || digitos.length === 14) && /^[\d.\-\/\s]+$/.test(nome)) {
        $('#crCpfCnpj').value = nome;
        mascaraCpfCnpj($('#crCpfCnpj'));
        $('#crTipo').value = digitos.length === 14 ? 'PJ' : 'PF';
      } else if (nome.includes('@')) {
        $('#crEmail').value = nome;
      } else {
        $('#crNome').value = nome;
      }
      resolver?.(null);
      overlay.classList.add('open');
      setTimeout(() => $('#crNome').focus(), 0);
      return new Promise((resolve) => { resolver = resolve; });
    },
  };
}

/**
 * Abre o cadastro rápido de contato (nome, CPF/CNPJ, telefone, e-mail) por cima da tela atual.
 * Resolve com o contato criado ({ id, nome, ... }) ou null se o usuário cancelar.
 */
export function cadastrarContatoRapido({ nome = '', tipoContato = 'Cliente' } = {}) {
  modalRapido ??= criarModalRapido();
  return modalRapido.abrir({ nome, tipoContato });
}
