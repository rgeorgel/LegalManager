// Gerador do CSS de tema escuro completo. Usado pelos presets escuros (js/presets.js) e pelo
// modo escuro do usuário (js/modo-cor.js via js/theme.js), que aplica esta base por cima de um
// tema claro do escritório. Fica num módulo próprio para o theme.js, carregado em toda página,
// não precisar importar o presets.js inteiro.

/**
 * Base de tema escuro completo (a mesma estratégia do Escuro/Dracula/Matrix:
 * superfícies, textos, selos, modais e os #fff fixos das páginas), gerada a
 * partir de uma paleta para não copiar o bloco inteiro em cada tema novo.
 *
 * - bg / surface / hover: fundo da página, dos cards e do hover/selecionado
 * - text / muted / placeholder / border
 * - onPrimary: texto sobre fundo var(--color-primary) (aba ativa, item selecionado)
 * - ok / err / warn / info / roxo / neutro: [fundo, texto] dos selos de status
 * - kanban: [pendente, andamento, concluída]
 */
export function darkCss(p) {
  const s = (sel, [bg, fg]) => `${sel} { background-color: ${bg} !important; color: ${fg} !important; }`;
  const sup = `background-color: ${p.surface} !important; color: ${p.text} !important; border-color: ${p.border} !important;`;
  return `
      :root {
        --color-bg: ${p.bg}; --color-surface: ${p.surface}; --color-text: ${p.text};
        --color-text-muted: ${p.muted}; --color-border: ${p.border};
        --color-primary-light: ${p.primaryLight} !important;
        --color-on-primary: ${p.onPrimary};
        --color-sidebar-text: ${p.sidebarText} !important;
        --hc-navy-text: var(--color-primary);
        --kanban-pendente: ${p.kanban[0]}; --kanban-andamento: ${p.kanban[1]}; --kanban-concluida: ${p.kanban[2]};
      }
      body { background: ${p.bg} !important; color: ${p.text} !important; }
      .header { background: ${p.surface} !important; border-bottom-color: ${p.border} !important; }
      .header-user, .auth-subtitle, label, .form-label, .lbl, .modal label, .modal .form-label { color: ${p.muted} !important; }
      .theme-card, .stat-card, .config-section, .form-section, .form-control, textarea, .preset-card,
      .table, table, table thead th, .card, .alert, .hp-card, .ci, .processo-card, .notif-card,
      .filter-bar select, .filter-bar input, .search-btn, .view-btn, .searchable-select-list,
      .row-menu-dropdown, #folderCtxMenu, .process-preview.loading,
      .modal, .modal-card, .modal-content, .pdf-modal-content, .modal-overlay > div, [id$="modal"] > div,
      div[style*="background:var(--color-surface)"], div[style*="background:#fff"], div[style*="background: #fff"],
      div[style*="background:white"], div[style*="background: white"] { ${sup} }
      .btn-secondary { background-color: transparent !important; color: ${p.text} !important; border-color: ${p.border} !important; }
      .btn-secondary:hover, .search-btn:hover, .view-btn:hover, .searchable-select-list li:hover,
      .row-menu-dropdown div:hover, #folderCtxMenu div:hover { background-color: ${p.hover} !important; }
      .row-menu-dropdown div, .row-menu-dropdown button, #folderCtxMenu div, #folderCtxMenu button { background-color: transparent !important; color: ${p.text} !important; }
      table tbody tr:hover { background-color: ${p.hover} !important; }
      .stat-card-value, div[style*="color:#1a56db"], div[style*="color:var(--color-primary)"] { color: var(--color-primary) !important; }
      hr, .card-divider, .hp-meta-bar { border-color: ${p.border} !important; }
      .card-divider, .hp-meta-bar { background-color: ${p.border} !important; }
      .hp-card .lbl, .hp-card .sub, .hp-evo-lbl, .hp-evo-val, .ci-sub, .ci-nums .cl, .notif-card .notif-msg, .notif-time,
      .logo-preview .empty { color: ${p.muted} !important; }
      .notif-card .notif-title, .modal-header, .modal-title, .modal-close { color: ${p.text} !important; }
      .notif-card.unread .notif-title { color: #fff !important; }
      .processo-card:hover { border-color: var(--color-primary) !important; }
      .logo-preview { background: ${p.bg} !important; border-color: ${p.border} !important; }
      .modal-overlay { background-color: ${p.overlay} !important; }
      .searchable-select-list li.selected { background-color: var(--color-primary) !important; color: ${p.onPrimary} !important; }

      button { color: inherit !important; }
      .filter-tab, .tab-btn, .view-btn { color: var(--color-text-muted) !important; }
      .filter-tab.active, .tab-btn.active { color: ${p.onPrimary} !important; }
      /* Selecionados com fundo primário: "button { color: inherit !important }" acima venceria a página */
      .view-btn.active { background-color: var(--color-primary) !important; border-color: var(--color-primary) !important; }
      .view-btn.active, .pc-seg button.on, .cp-tipo-btn.on, .sort-chip.active, .pagination button.active,
      .page-btn.active, .page-btn:hover:not(:disabled), .filtro.ativo, .d2-seg button.active,
      .billing-opt.active { color: ${p.onPrimary} !important; }
      .filter-tab:hover, .tab-btn:hover { color: var(--color-primary) !important; }
      .filter-tab .tab-count { background-color: rgba(255,255,255,0.12) !important; color: inherit !important; }
      .filter-tab.active .tab-count { background-color: rgba(0,0,0,0.18) !important; }

      input, select, textarea, button { color-scheme: dark; }
      ::placeholder, input::placeholder, textarea::placeholder { color: ${p.placeholder} !important; }
      select option { background-color: ${p.surface} !important; color: ${p.text} !important; }

      ${s('.badge-ok, .badge-green, .status-ativo', p.ok)}
      ${s('.badge-err, .badge-red, .status-encerrado', p.err)}
      ${s('.badge-warn, .badge-yellow, .status-suspenso', p.warn)}
      ${s('.badge-blue, .badge-proc', p.info)}
      ${s('.badge-purple', p.roxo)}
      ${s('.badge-gray, .badge-muted, .status-arquivado', p.neutro)}
      .process-preview.encontrado { background: ${p.ok[0]} !important; border-color: ${p.ok[1]} !important; color: ${p.ok[1]} !important; }
      .process-preview.nao-encontrado { background: ${p.warn[0]} !important; border-color: ${p.warn[1]} !important; color: ${p.warn[1]} !important; }
      ${s('.tipo-peticao', p.info)}
      ${s('.tipo-decisao', p.warn)}
      ${s('.tipo-contrato', p.ok)}
      ${s('.tipo-prova', p.err)}
      ${s('.tipo-modelo', p.roxo)}
      ${s('.tipo-outro', p.neutro)}`;
}

/**
 * Paleta "ardósia" do modo escuro do usuário — a mesma do preset Escuro. A primária e o texto
 * sobre ela variam com o tema do escritório, então entram por parâmetro.
 */
export function paletaModoEscuro({ primaryLight, onPrimary }) {
  return {
    bg: '#0f172a', surface: '#1e293b', hover: '#334155', border: '#334155',
    text: '#e2e8f0', muted: '#94a3b8', placeholder: '#64748b', sidebarText: '#cbd5e1',
    primaryLight, onPrimary, overlay: 'rgba(0,0,0,0.7)',
    ok: ['#064e3b', '#6ee7b7'], err: ['#7f1d1d', '#fca5a5'], warn: ['#78350f', '#fcd34d'],
    info: ['#1e3a8a', '#93c5fd'], roxo: ['#4c1d95', '#c4b5fd'], neutro: ['rgba(148,163,184,0.18)', '#94a3b8'],
    kanban: ['#cbd5e1', '#93c5fd', '#6ee7b7'],
  };
}
