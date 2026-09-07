/**
 * Temas predefinidos do Causify.
 * Cada preset é um pacote completo de cores + layoutMode + customCss.
 * Aplicar um preset = preencher o formulário de personalização com seus
 * valores. O backend (PUT /tenants/current/theme) não precisa saber nada
 * sobre presets — eles são apenas um ponto de partida no cliente.
 */
export const PRESETS = [
  {
    id: 'default',
    name: 'Padrão',
    description: 'Visual clássico do Causify',
    primaryColor: '#1a56db',
    sidebarColor: '#1e2a3b',
    accentColor: '#057a55',
    layoutMode: 'default',
    customCss: '',
  },
  {
    id: 'dark',
    name: 'Escuro',
    description: 'Modo dark para reduzir cansaço visual',
    primaryColor: '#60a5fa',
    sidebarColor: '#0f172a',
    accentColor: '#10b981',
    layoutMode: 'default',
    // Dark mode completo: bg escuro + superfície cinza-escuro + texto claro.
    // Sobrescreve #fff hardcoded em styles.css e as variáveis --color-* que
    // o tema com sidebar escura + body claro deixava visualmente quebrado.
    customCss: `
      :root {
        --color-bg: #0f172a;
        --color-surface: #1e293b;
        --color-text: #e2e8f0;
        --color-text-muted: #94a3b8;
        --color-border: #334155;
        --color-primary-light: #1e3a8a;
        /* Honorários: --hc-navy é usado como cor de texto sobre superfícies
           claras e também como fundo de botões/tabelas — clarear apenas o
           uso de texto (--hc-navy-text) preserva os fundos navy intactos. */
        --hc-navy-text: var(--color-primary);
        /* Tarefas: títulos das colunas do Kanban (Pendente/Andamento/Concluída) */
        --kanban-pendente: #cbd5e1;
        --kanban-andamento: #93c5fd;
        --kanban-concluida: #6ee7b7;
      }
      body { background: #0f172a !important; color: #e2e8f0 !important; }
      .header { background: #1e293b !important; border-bottom-color: #334155 !important; }
      .header-user, .auth-subtitle { color: #94a3b8 !important; }
      .theme-card, .stat-card, .config-section, .form-section,
      .form-control, textarea, .preset-card,
      .table, table, table thead th, .card, .modal-content, .alert,
      div[style*="background:var(--color-surface)"] {
        background-color: #1e293b !important;
        color: #e2e8f0 !important;
        border-color: #334155 !important;
      }
      .btn-secondary {
        background-color: transparent !important;
        color: #e2e8f0 !important;
        border-color: #334155 !important;
      }
      .btn-secondary:hover { background-color: #334155 !important; }
      table tbody tr:hover { background-color: #0f172a !important; }
      .stat-card-value, div[style*="color:#1a56db"], div[style*="color:var(--color-primary)"] { color: var(--color-primary) !important; }
      hr, .badge-gray { border-color: #334155 !important; }
      .badge-gray { background: #334155 !important; color: #94a3b8 !important; }
      .badge-yellow, .badge-blue, .badge-purple, .badge-green, .badge-red {
        color: #f8fafc !important;
      }
      ::placeholder { color: #64748b !important; }
      .logo-preview { background: #0f172a !important; border-color: #334155 !important; }
      .logo-preview .empty { color: #94a3b8 !important; }

      /* ── Honorários-Contratos ─────────────────────────────────── */
      .hp-card, .ci { background-color: #1e293b !important; color: #e2e8f0 !important; border-color: #334155 !important; }
      .hp-card .lbl, .hp-card .sub, .hp-evo-lbl, .hp-evo-val, .ci-sub, .ci-nums .cl { color: #94a3b8 !important; }
      .hp-meta-bar { background-color: #334155 !important; }
      .badge-ok { background-color: #064e3b !important; color: #6ee7b7 !important; }
      .badge-err { background-color: #7f1d1d !important; color: #fca5a5 !important; }
      .badge-warn { background-color: #78350f !important; color: #fcd34d !important; }
      .badge-muted { background-color: #334155 !important; color: #cbd5e1 !important; }
      .badge-proc { background-color: #4c1d95 !important; color: #c4b5fd !important; }
      .filter-bar select, .filter-bar input { background-color: #1e293b !important; color: #e2e8f0 !important; border-color: #334155 !important; }

      /* ── Processos ───────────────────────────────────────────── */
      .processo-card { background-color: #1e293b !important; color: #e2e8f0 !important; border-color: #334155 !important; }
      .processo-card:hover { border-color: var(--color-primary) !important; }
      .card-divider { background-color: #334155 !important; }
      .status-ativo     { background: #064e3b !important; color: #6ee7b7 !important; }
      .status-suspenso  { background: #78350f !important; color: #fcd34d !important; }
      .status-arquivado { background: #334155 !important; color: #cbd5e1 !important; }
      .status-encerrado { background: #7f1d1d !important; color: #fca5a5 !important; }
      .process-preview.encontrado { background: #064e3b !important; border-color: #047857 !important; color: #a7f3d0 !important; }
      .process-preview.nao-encontrado { background: #78350f !important; border-color: #b45309 !important; color: #fde68a !important; }
      .process-preview.loading { background: #1e293b !important; border-color: #334155 !important; color: #94a3b8 !important; }
      .search-btn, .view-btn { background-color: #1e293b !important; color: #e2e8f0 !important; border-color: #334155 !important; }
      .search-btn:hover, .view-btn:hover { background-color: #334155 !important; }
      .searchable-select-list { background-color: #1e293b !important; color: #e2e8f0 !important; border-color: #334155 !important; }
      .searchable-select-list li:hover { background-color: #334155 !important; }
      .searchable-select-list li.selected { background-color: var(--color-primary) !important; color: #fff !important; }

      /* ── Agenda ──────────────────────────────────────────────── */
      .semana-grid { background-color: #1e293b !important; }
      .semana-header { background-color: #0f172a !important; color: #cbd5e1 !important; border-color: #334155 !important; }
      .semana-header.hoje { background-color: var(--color-primary) !important; color: #fff !important; }
      .hora-label { color: #94a3b8 !important; border-color: #334155 !important; }
      .agenda-item { background-color: #1e293b !important; color: #e2e8f0 !important; border-color: #334155 !important; }
      .agenda-item:active { background-color: #0f172a !important; }

      /* ── Botões precisam herdar cor (fix Alertas e outros com texto preto) ─── */
      button { color: inherit !important; }
      .filter-tab, .tab-btn, .view-btn { color: var(--color-text-muted) !important; }
      .filter-tab.active, .tab-btn.active { color: #fff !important; }
      .filter-tab:hover, .tab-btn:hover { color: var(--color-primary) !important; }
      .filter-tab .tab-count { background-color: rgba(255,255,255,0.18) !important; color: inherit !important; }
      .filter-tab.active .tab-count { background-color: rgba(255,255,255,0.28) !important; }

      /* ── Inputs/selects/textareas ────────────────────────────────── */
      input, select, textarea, button { color-scheme: dark; }
      input::placeholder, textarea::placeholder { color: #94a3b8 !important; }
      select option { background-color: #1e293b !important; color: #e2e8f0 !important; }

      /* ── Modais (catches inline background:#fff em financeiro, timesheet, contatos) ── */
      .modal,
      .modal-card,
      .modal-content,
      .pdf-modal-content,
      .modal-overlay > div,
      [id$="modal"] > div,
      div[style*="background:#fff"],
      div[style*="background: #fff"],
      div[style*="background:white"],
      div[style*="background: white"] {
        background-color: #1e293b !important;
        color: #e2e8f0 !important;
        border-color: #334155 !important;
      }
      .modal-header, .modal-title, .modal-close { color: #e2e8f0 !important; }
      .modal-overlay { background-color: rgba(0,0,0,0.7) !important; }
      .modal label, .modal .form-label { color: #94a3b8 !important; }

      /* ── Labels e textos secundários em formulários ──────────────── */
      label, .form-label, .lbl { color: #94a3b8 !important; }

      /* ── Configurações > Usuários / Feriados / Assinaturas: badges de PERFIL/STATUS ── */
      .badge-blue { background-color: #1e3a8a !important; color: #93c5fd !important; }
      .badge-green { background-color: #064e3b !important; color: #6ee7b7 !important; }
      .badge-red   { background-color: #7f1d1d !important; color: #fca5a5 !important; }
      .badge-yellow { background-color: #78350f !important; color: #fcd34d !important; }
      .badge-purple { background-color: #4c1d95 !important; color: #c4b5fd !important; }

      /* ── Processos: status badges (cards) ───────────────────────── */
      .status-ativo     { background-color: #064e3b !important; color: #6ee7b7 !important; }
      .status-suspenso  { background-color: #78350f !important; color: #fcd34d !important; }
      .status-arquivado { background-color: rgba(148,163,184,0.18) !important; color: #94a3b8 !important; }
      .status-encerrado { background-color: #7f1d1d !important; color: #fca5a5 !important; }

      /* ── Contatos: chips de tipo de documento ───────────────────── */
      .tipo-peticao { background-color: rgba(59,130,246,0.20) !important; color: #93c5fd !important; }
      .tipo-decisao { background-color: rgba(245,158,11,0.20) !important; color: #fbbf24 !important; }
      .tipo-contrato { background-color: rgba(16,185,129,0.20) !important; color: #6ee7b7 !important; }
      .tipo-prova { background-color: rgba(236,72,153,0.20) !important; color: #f9a8d4 !important; }
      .tipo-modelo { background-color: rgba(139,92,246,0.20) !important; color: #c4b5fd !important; }
      .tipo-outro { background-color: rgba(148,163,184,0.20) !important; color: #94a3b8 !important; }

      /* ── Contatos: context menus de linha e pasta (inline #fff) ───── */
      .row-menu-dropdown, #folderCtxMenu {
        background-color: #1e293b !important;
        color: #e2e8f0 !important;
        border-color: #334155 !important;
      }
      .row-menu-dropdown div, .row-menu-dropdown button,
      #folderCtxMenu div, #folderCtxMenu button {
        background-color: transparent !important;
        color: #e2e8f0 !important;
      }
      .row-menu-dropdown div:hover, #folderCtxMenu div:hover {
        background-color: #334155 !important;
      }

      /* ── Alertas: notif cards ────────────────────────────────────── */
      .notif-card { background-color: #1e293b !important; border-color: #334155 !important; color: #e2e8f0 !important; }
      .notif-card .notif-title { color: #e2e8f0 !important; }
      .notif-card.unread .notif-title { color: #fff !important; }
      .notif-card .notif-msg, .notif-time { color: #94a3b8 !important; }
    `.trim(),
  },
  {
    id: 'liquid-glass',
    name: 'Liquid Glass',
    description: 'Efeito vidro fosco no sidebar e cards',
    primaryColor: '#0891b2',
    sidebarColor: '#0e7490',
    accentColor: '#22d3ee',
    layoutMode: 'default',
    customCss: `
      .sidebar { background: rgba(14, 116, 144, 0.78) !important;
                 backdrop-filter: blur(20px) saturate(180%);
                 -webkit-backdrop-filter: blur(20px) saturate(180%);
                 border-right: 1px solid rgba(255,255,255,0.15); }
      .header  { background: rgba(255,255,255,0.72) !important;
                 backdrop-filter: blur(20px) saturate(180%);
                 -webkit-backdrop-filter: blur(20px) saturate(180%);
                 border-bottom: 1px solid rgba(255,255,255,0.35); }
      .btn-primary { background-image: linear-gradient(135deg, var(--color-primary), #22d3ee);
                     border: none;
                     box-shadow: 0 4px 14px rgba(34, 211, 238, 0.32); }
      .stat-card, .config-section, .theme-card {
        background: rgba(255,255,255,0.72) !important;
        backdrop-filter: blur(12px);
        -webkit-backdrop-filter: blur(12px);
        border: 1px solid rgba(255,255,255,0.45);
        box-shadow: 0 4px 18px rgba(15, 23, 42, 0.06);
      }
      .form-control, .preset-card { background: rgba(255,255,255,0.65) !important; }
    `.trim(),
  },
  {
    id: 'dracula',
    name: 'Dracula',
    description: 'Tema escuro com roxos vibrantes',
    primaryColor: '#bd93f9',
    sidebarColor: '#282a36',
    accentColor: '#ff79c6',
    layoutMode: 'default',
    // Dracula full theme: mesma estratégia do Escuro, paleta Dracula
    // (background #21222c, current-line #282a36, foreground #f8f8f2,
    //  comment #6272a4, comment-dim #44475a).
    customCss: `
      :root {
        --color-bg: #21222c;
        --color-surface: #282a36;
        --color-text: #f8f8f2;
        --color-text-muted: #6272a4;
        --color-border: #44475a;
        --color-primary-light: #4a3868;
        /* Honorários: --hc-navy é usado como cor de texto sobre superfícies
           claras e também como fundo de botões/tabelas — clarear apenas o
           uso de texto (--hc-navy-text) preserva os fundos navy intactos. */
        --hc-navy-text: var(--color-primary);
        /* Tarefas: títulos das colunas do Kanban (Pendente/Andamento/Concluída) */
        --kanban-pendente: #bd93f9;
        --kanban-andamento: #8be9fd;
        --kanban-concluida: #50fa7b;
      }
      body { background: #21222c !important; color: #f8f8f2 !important; }
      .header { background: #21222c !important; border-bottom-color: #44475a !important; }
      .header-user, .auth-subtitle { color: #6272a4 !important; }
      .theme-card, .stat-card, .config-section, .form-section,
      .form-control, textarea, .preset-card,
      .table, table, table thead th, .card, .modal-content, .alert,
      div[style*="background:var(--color-surface)"] {
        background-color: #282a36 !important;
        color: #f8f8f2 !important;
        border-color: #44475a !important;
      }
      .btn-secondary {
        background-color: transparent !important;
        color: #f8f8f2 !important;
        border-color: #6272a4 !important;
      }
      .btn-secondary:hover { background-color: #44475a !important; }
      table tbody tr:hover { background-color: #21222c !important; }
      .stat-card-value, div[style*="color:#1a56db"], div[style*="color:var(--color-primary)"] { color: var(--color-primary) !important; }
      hr, .badge-gray { border-color: #44475a !important; }
      .badge-gray { background: #44475a !important; color: #6272a4 !important; }
      .badge-yellow, .badge-blue, .badge-purple, .badge-green, .badge-red {
        color: #f8f8f2 !important;
      }
      ::placeholder { color: #6272a4 !important; }
      .logo-preview { background: #21222c !important; border-color: #44475a !important; }
      .logo-preview .empty { color: #6272a4 !important; }

      /* ── Honorários-Contratos ─────────────────────────────────── */
      .hp-card, .ci { background-color: #282a36 !important; color: #f8f8f2 !important; border-color: #44475a !important; }
      .hp-card .lbl, .hp-card .sub, .hp-evo-lbl, .hp-evo-val, .ci-sub, .ci-nums .cl { color: #6272a4 !important; }
      .hp-meta-bar { background-color: #44475a !important; }
      .badge-ok { background-color: #1a4023 !important; color: #a7f3d0 !important; }
      .badge-err { background-color: #5a1f24 !important; color: #fca5a5 !important; }
      .badge-warn { background-color: #5a3f15 !important; color: #fcd34d !important; }
      .badge-muted { background-color: #44475a !important; color: #bd93f9 !important; }
      .badge-proc { background-color: #3a2c5e !important; color: #c4b5fd !important; }
      .filter-bar select, .filter-bar input { background-color: #282a36 !important; color: #f8f8f2 !important; border-color: #44475a !important; }

      /* ── Processos ───────────────────────────────────────────── */
      .processo-card { background-color: #282a36 !important; color: #f8f8f2 !important; border-color: #44475a !important; }
      .processo-card:hover { border-color: var(--color-primary) !important; }
      .card-divider { background-color: #44475a !important; }
      .status-ativo     { background: #1a4023 !important; color: #a7f3d0 !important; }
      .status-suspenso  { background: #5a3f15 !important; color: #fcd34d !important; }
      .status-arquivado { background: #44475a !important; color: #bd93f9 !important; }
      .status-encerrado { background: #5a1f24 !important; color: #fca5a5 !important; }
      .process-preview.encontrado { background: #1a4023 !important; border-color: #047857 !important; color: #a7f3d0 !important; }
      .process-preview.nao-encontrado { background: #5a3f15 !important; border-color: #b45309 !important; color: #fde68a !important; }
      .process-preview.loading { background: #282a36 !important; border-color: #44475a !important; color: #6272a4 !important; }
      .search-btn, .view-btn { background-color: #282a36 !important; color: #f8f8f2 !important; border-color: #44475a !important; }
      .search-btn:hover, .view-btn:hover { background-color: #44475a !important; }
      .searchable-select-list { background-color: #282a36 !important; color: #f8f8f2 !important; border-color: #44475a !important; }
      .searchable-select-list li:hover { background-color: #44475a !important; }
      .searchable-select-list li.selected { background-color: var(--color-primary) !important; color: #fff !important; }

      /* ── Agenda ──────────────────────────────────────────────── */
      .semana-grid { background-color: #282a36 !important; }
      .semana-header { background-color: #21222c !important; color: #bd93f9 !important; border-color: #44475a !important; }
      .semana-header.hoje { background-color: var(--color-primary) !important; color: #fff !important; }
      .hora-label { color: #6272a4 !important; border-color: #44475a !important; }
      .agenda-item { background-color: #282a36 !important; color: #f8f8f2 !important; border-color: #44475a !important; }
      .agenda-item:active { background-color: #21222c !important; }

      /* ── Botões precisam herdar cor (fix Alertas e outros com texto preto) ─── */
      button { color: inherit !important; }
      .filter-tab, .tab-btn, .view-btn { color: var(--color-text-muted) !important; }
      .filter-tab.active, .tab-btn.active { color: #fff !important; }
      .filter-tab:hover, .tab-btn:hover { color: var(--color-primary) !important; }
      .filter-tab .tab-count { background-color: rgba(255,255,255,0.18) !important; color: inherit !important; }
      .filter-tab.active .tab-count { background-color: rgba(255,255,255,0.28) !important; }

      /* ── Inputs/selects/textareas ────────────────────────────────── */
      input, select, textarea, button { color-scheme: dark; }
      input::placeholder, textarea::placeholder { color: #6272a4 !important; }
      select option { background-color: #282a36 !important; color: #f8f8f2 !important; }

      /* ── Modais (catches inline background:#fff em financeiro, timesheet, contatos) ── */
      .modal,
      .modal-card,
      .modal-content,
      .pdf-modal-content,
      .modal-overlay > div,
      [id$="modal"] > div,
      div[style*="background:#fff"],
      div[style*="background: #fff"],
      div[style*="background:white"],
      div[style*="background: white"] {
        background-color: #282a36 !important;
        color: #f8f8f2 !important;
        border-color: #44475a !important;
      }
      .modal-header, .modal-title, .modal-close { color: #f8f8f2 !important; }
      .modal-overlay { background-color: rgba(0,0,0,0.7) !important; }
      .modal label, .modal .form-label { color: #6272a4 !important; }

      /* ── Labels e textos secundários em formulários ──────────────── */
      label, .form-label, .lbl { color: #6272a4 !important; }

      /* ── Configurações > Usuários / Feriados / Assinaturas: badges de PERFIL/STATUS ── */
      .badge-blue { background-color: #4a3868 !important; color: #bd93f9 !important; }
      .badge-green { background-color: #1a4023 !important; color: #a7f3d0 !important; }
      .badge-red   { background-color: #5a1f24 !important; color: #fca5a5 !important; }
      .badge-yellow { background-color: #5a3f15 !important; color: #fcd34d !important; }
      .badge-purple { background-color: #3a2c5e !important; color: #c4b5fd !important; }

      /* ── Processos: status badges (cards) ───────────────────────── */
      .status-ativo     { background-color: #1a4023 !important; color: #a7f3d0 !important; }
      .status-suspenso  { background-color: #5a3f15 !important; color: #fcd34d !important; }
      .status-arquivado { background-color: rgba(189,147,249,0.18) !important; color: #bd93f9 !important; }
      .status-encerrado { background-color: #5a1f24 !important; color: #fca5a5 !important; }

      /* ── Contatos: chips de tipo de documento ───────────────────── */
      .tipo-peticao { background-color: rgba(189,147,249,0.22) !important; color: #bd93f9 !important; }
      .tipo-decisao { background-color: rgba(255,121,198,0.22) !important; color: #ff79c6 !important; }
      .tipo-contrato { background-color: rgba(80,250,123,0.22) !important; color: #50fa7b !important; }
      .tipo-prova { background-color: rgba(255,121,198,0.22) !important; color: #ff79c6 !important; }
      .tipo-modelo { background-color: rgba(139,233,253,0.22) !important; color: #8be9fd !important; }
      .tipo-outro { background-color: rgba(98,114,164,0.22) !important; color: #6272a4 !important; }

      /* ── Contatos: context menus de linha e pasta (inline #fff) ───── */
      .row-menu-dropdown, #folderCtxMenu {
        background-color: #282a36 !important;
        color: #f8f8f2 !important;
        border-color: #44475a !important;
      }
      .row-menu-dropdown div, .row-menu-dropdown button,
      #folderCtxMenu div, #folderCtxMenu button {
        background-color: transparent !important;
        color: #f8f8f2 !important;
      }
      .row-menu-dropdown div:hover, #folderCtxMenu div:hover {
        background-color: #44475a !important;
      }

      /* ── Alertas: notif cards ────────────────────────────────────── */
      .notif-card { background-color: #282a36 !important; border-color: #44475a !important; color: #f8f8f2 !important; }
      .notif-card .notif-title { color: #f8f8f2 !important; }
      .notif-card.unread .notif-title { color: #fff !important; }
      .notif-card .notif-msg, .notif-time { color: #6272a4 !important; }
    `.trim(),
  },
  {
    id: 'ocean',
    name: 'Ocean',
    description: 'Azul profundo e turquesa',
    primaryColor: '#0891b2',
    sidebarColor: '#0c4a6e',
    accentColor: '#06b6d4',
    layoutMode: 'default',
    customCss: '',
  },
  {
    id: 'sunset',
    name: 'Sunset',
    description: 'Tons quentes de pôr-do-sol',
    primaryColor: '#f97316',
    sidebarColor: '#7c2d12',
    accentColor: '#fbbf24',
    layoutMode: 'default',
    customCss: '',
  },
  {
    id: 'forest',
    name: 'Floresta',
    description: 'Tons naturais de verde',
    primaryColor: '#16a34a',
    sidebarColor: '#14532d',
    accentColor: '#84cc16',
    layoutMode: 'default',
    customCss: '',
  },
  {
    id: 'compact',
    name: 'Compacto',
    description: 'Densidade maior de informação na tela',
    primaryColor: '#1a56db',
    sidebarColor: '#1e2a3b',
    accentColor: '#057a55',
    layoutMode: 'compact',
    customCss: '',
  },
];

/**
 * Retorna o preset cujo conjunto de cores bate com o tema salvo, ou null
 * se o tema divergir de qualquer preset (tema customizado pelo usuário).
 */
export function findPresetByTheme(theme) {
  if (!theme) return null;
  return PRESETS.find(p =>
    sameHex(p.primaryColor, theme.primaryColor) &&
    sameHex(p.sidebarColor, theme.sidebarColor) &&
    sameHex(p.accentColor, theme.accentColor) &&
    p.layoutMode === (theme.layoutMode || 'default')
  ) || null;
}

function sameHex(a, b) {
  return (a || '').toLowerCase() === (b || '').toLowerCase();
}