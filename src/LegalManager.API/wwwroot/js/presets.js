/**
 * Temas predefinidos do Causify.
 * Cada preset é um pacote completo de cores + layoutMode + customCss.
 * Aplicar um preset = preencher o formulário de personalização com seus
 * valores. O backend (PUT /tenants/current/theme) não precisa saber nada
 * sobre presets — eles são apenas um ponto de partida no cliente.
 *
 * `dark` é só um rótulo usado pela UI (tema.html) para agrupar os cards em
 * "Temas claros" / "Temas escuros" — não é enviado ao backend nem usado por
 * findPresetByTheme (que casa por cores).
 */
export const PRESETS = [
  {
    id: 'default',
    name: 'Padrão',
    description: 'Visual clássico do Causify',
    dark: false,
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
    dark: true,
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
    dark: false,
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
    id: 'moderno',
    name: 'Moderno',
    description: 'Visual SaaS contemporâneo, sidebar escura e gradientes',
    dark: false,
    primaryColor: '#4f46e5',
    sidebarColor: '#0b1120',
    accentColor: '#06b6d4',
    layoutMode: 'default',
    // Reskin "claro" do app: mantém --color-bg/--color-surface padrão, mas
    // troca sidebar/botões/cards para um visual mais atual (cantos maiores,
    // gradientes sutis, pill nav, sombras coloridas). Não sobrescreve
    // --color-bg, então continua sendo detectado como tema claro.
    customCss: `
      :root {
        --radius: 14px;
        --shadow: 0 2px 10px rgba(15,23,42,0.06), 0 1px 2px rgba(15,23,42,0.04);
        --shadow-md: 0 16px 40px rgba(79,70,229,0.14), 0 4px 12px rgba(15,23,42,0.06);
      }
      .sidebar {
        background: linear-gradient(180deg, var(--color-sidebar) 0%, #060a14 100%) !important;
        border-right: 1px solid rgba(255,255,255,0.06);
      }
      .sidebar-nav li a {
        border-left: none !important;
        border-radius: 8px;
        margin: 2px 8px;
        width: calc(100% - 16px);
      }
      .sidebar-nav li a.active, .sidebar-nav li a:hover {
        background: linear-gradient(90deg, rgba(79,70,229,0.35), rgba(6,182,212,0.12)) !important;
        color: #fff !important;
      }
      .header { backdrop-filter: saturate(160%) blur(6px); }
      .btn-primary {
        background-image: linear-gradient(135deg, var(--color-primary), var(--color-accent));
        border: none;
        box-shadow: 0 6px 18px rgba(79,70,229,0.32);
      }
      .btn-primary:hover { filter: brightness(1.07); box-shadow: 0 8px 22px rgba(79,70,229,0.4); }
      .card, .stat-card, .config-section, .form-section, .theme-card, .modal, .modal-content, .preset-card {
        border-radius: 16px !important;
      }
      .stat-card { transition: transform .15s ease, box-shadow .15s ease; }
      .stat-card:hover { transform: translateY(-2px); box-shadow: var(--shadow-md); }
      .page-title {
        background: linear-gradient(135deg, var(--color-sidebar), var(--color-primary));
        -webkit-background-clip: text;
        background-clip: text;
        -webkit-text-fill-color: transparent;
        color: transparent;
      }
      .badge { border-radius: 999px; }
      .tab-btn.active { border-bottom-color: transparent; position: relative; }
      .tab-btn.active::after {
        content: '';
        position: absolute;
        left: 20px; right: 20px; bottom: -2px;
        height: 2px;
        background: linear-gradient(90deg, var(--color-primary), var(--color-accent));
      }
      ::-webkit-scrollbar { width: 10px; height: 10px; }
      ::-webkit-scrollbar-thumb { background: var(--color-border); border-radius: 8px; }
      ::-webkit-scrollbar-thumb:hover { background: var(--color-text-muted); }
    `.trim(),
  },
  {
    id: 'dracula',
    name: 'Dracula',
    description: 'Tema escuro com roxos vibrantes',
    dark: true,
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
    id: 'cyberpunk',
    name: 'Cyberpunk',
    description: 'Neon ciano e magenta com glow pulsante, scanline e glitch',
    dark: true,
    primaryColor: '#00f0ff',
    sidebarColor: '#0a0014',
    accentColor: '#ff2bd6',
    layoutMode: 'default',
    // Dark theme completo (mesma estratégia do Escuro/Dracula) com paleta
    // neon (bg #05010f, surface #120a24, borda violeta #2d1b4e) + glow
    // pulsante em sidebar/botão/logo, scanline varrendo a sidebar e glitch
    // periódico no título (todas param via prefers-reduced-motion).
    // --color-primary é ciano muito claro: onde o preset usa
    // background-color: var(--color-primary) o texto é escurecido
    // (em vez do #fff usado nos outros dark themes) para manter contraste.
    customCss: `
      :root {
        --color-bg: #05010f;
        --color-surface: #120a24;
        --color-text: #e8f4ff;
        --color-text-muted: #9a8cc9;
        --color-border: #2d1b4e;
        --color-primary-light: #062836;
        --hc-navy-text: var(--color-primary);
        --kanban-pendente: #ff6ec7;
        --kanban-andamento: #22e5ff;
        --kanban-concluida: #39ff88;
      }
      body { background: #05010f !important; color: #e8f4ff !important; }
      .header { background: #120a24 !important; border-bottom-color: #2d1b4e !important; }
      .header-user, .auth-subtitle { color: #9a8cc9 !important; }
      .theme-card, .stat-card, .config-section, .form-section,
      .form-control, textarea, .preset-card,
      .table, table, table thead th, .card, .modal-content, .alert,
      div[style*="background:var(--color-surface)"] {
        background-color: #120a24 !important;
        color: #e8f4ff !important;
        border-color: #2d1b4e !important;
      }
      .btn-secondary {
        background-color: transparent !important;
        color: #e8f4ff !important;
        border-color: #2d1b4e !important;
      }
      .btn-secondary:hover { background-color: #2d1b4e !important; }
      table tbody tr:hover { background-color: #05010f !important; }
      .stat-card-value, div[style*="color:#1a56db"], div[style*="color:var(--color-primary)"] { color: var(--color-primary) !important; }
      hr, .badge-gray { border-color: #2d1b4e !important; }
      .badge-gray { background: #2d1b4e !important; color: #9a8cc9 !important; }
      .badge-yellow, .badge-blue, .badge-purple, .badge-green, .badge-red {
        color: #f5f3ff !important;
      }
      ::placeholder { color: #6b5b95 !important; }
      .logo-preview { background: #05010f !important; border-color: #2d1b4e !important; }
      .logo-preview .empty { color: #9a8cc9 !important; }

      /* ── Honorários-Contratos ─────────────────────────────────── */
      .hp-card, .ci { background-color: #120a24 !important; color: #e8f4ff !important; border-color: #2d1b4e !important; }
      .hp-card .lbl, .hp-card .sub, .hp-evo-lbl, .hp-evo-val, .ci-sub, .ci-nums .cl { color: #9a8cc9 !important; }
      .hp-meta-bar { background-color: #2d1b4e !important; }
      .badge-ok { background-color: #053b2e !important; color: #39ff88 !important; }
      .badge-err { background-color: #4a0f2e !important; color: #ff6ec7 !important; }
      .badge-warn { background-color: #4a2f0a !important; color: #ffd166 !important; }
      .badge-muted { background-color: #2d1b4e !important; color: #c9b8ff !important; }
      .badge-proc { background-color: #2a0a4a !important; color: #c9a3ff !important; }
      .filter-bar select, .filter-bar input { background-color: #120a24 !important; color: #e8f4ff !important; border-color: #2d1b4e !important; }

      /* ── Processos ───────────────────────────────────────────── */
      .processo-card { background-color: #120a24 !important; color: #e8f4ff !important; border-color: #2d1b4e !important; }
      .processo-card:hover { border-color: var(--color-primary) !important; box-shadow: 0 0 12px rgba(0,240,255,0.35) !important; }
      .card-divider { background-color: #2d1b4e !important; }
      .status-ativo     { background: #053b2e !important; color: #39ff88 !important; }
      .status-suspenso  { background: #4a2f0a !important; color: #ffd166 !important; }
      .status-arquivado { background: #2d1b4e !important; color: #9a8cc9 !important; }
      .status-encerrado { background: #4a0f2e !important; color: #ff6ec7 !important; }
      .process-preview.encontrado { background: #053b2e !important; border-color: #0d9668 !important; color: #7dffc4 !important; }
      .process-preview.nao-encontrado { background: #4a2f0a !important; border-color: #b8860b !important; color: #ffe08a !important; }
      .process-preview.loading { background: #120a24 !important; border-color: #2d1b4e !important; color: #9a8cc9 !important; }
      .search-btn, .view-btn { background-color: #120a24 !important; color: #e8f4ff !important; border-color: #2d1b4e !important; }
      .search-btn:hover, .view-btn:hover { background-color: #2d1b4e !important; }
      .searchable-select-list { background-color: #120a24 !important; color: #e8f4ff !important; border-color: #2d1b4e !important; }
      .searchable-select-list li:hover { background-color: #2d1b4e !important; }
      .searchable-select-list li.selected { background-color: var(--color-primary) !important; color: #05010f !important; }

      /* ── Agenda ──────────────────────────────────────────────── */
      .semana-grid { background-color: #120a24 !important; }
      .semana-header { background-color: #05010f !important; color: #c9b8ff !important; border-color: #2d1b4e !important; }
      .semana-header.hoje { background-color: var(--color-primary) !important; color: #05010f !important; }
      .hora-label { color: #9a8cc9 !important; border-color: #2d1b4e !important; }
      .agenda-item { background-color: #120a24 !important; color: #e8f4ff !important; border-color: #2d1b4e !important; }
      .agenda-item:active { background-color: #05010f !important; }

      /* ── Botões precisam herdar cor (fix Alertas e outros com texto preto) ─── */
      button { color: inherit !important; }
      .filter-tab, .tab-btn, .view-btn { color: var(--color-text-muted) !important; }
      .filter-tab.active, .tab-btn.active { color: #fff !important; }
      .filter-tab:hover, .tab-btn:hover { color: var(--color-primary) !important; }
      .filter-tab .tab-count { background-color: rgba(255,255,255,0.18) !important; color: inherit !important; }
      .filter-tab.active .tab-count { background-color: rgba(255,255,255,0.28) !important; }

      /* ── Inputs/selects/textareas ────────────────────────────────── */
      input, select, textarea, button { color-scheme: dark; }
      input::placeholder, textarea::placeholder { color: #6b5b95 !important; }
      select option { background-color: #120a24 !important; color: #e8f4ff !important; }

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
        background-color: #120a24 !important;
        color: #e8f4ff !important;
        border-color: #2d1b4e !important;
      }
      .modal-header, .modal-title, .modal-close { color: #e8f4ff !important; }
      .modal-overlay { background-color: rgba(5,1,15,0.82) !important; }
      .modal label, .modal .form-label { color: #9a8cc9 !important; }

      /* ── Labels e textos secundários em formulários ──────────────── */
      label, .form-label, .lbl { color: #9a8cc9 !important; }

      /* ── Configurações > Usuários / Feriados / Assinaturas: badges de PERFIL/STATUS ── */
      .badge-blue { background-color: #062836 !important; color: #7de3ff !important; }
      .badge-green { background-color: #053b2e !important; color: #39ff88 !important; }
      .badge-red   { background-color: #4a0f2e !important; color: #ff6ec7 !important; }
      .badge-yellow { background-color: #4a2f0a !important; color: #ffd166 !important; }
      .badge-purple { background-color: #2a0a4a !important; color: #c9a3ff !important; }

      /* ── Processos: status badges (cards) ───────────────────────── */
      .status-ativo     { background-color: #053b2e !important; color: #39ff88 !important; }
      .status-suspenso  { background-color: #4a2f0a !important; color: #ffd166 !important; }
      .status-arquivado { background-color: rgba(154,140,201,0.18) !important; color: #9a8cc9 !important; }
      .status-encerrado { background-color: #4a0f2e !important; color: #ff6ec7 !important; }

      /* ── Contatos: chips de tipo de documento ───────────────────── */
      .tipo-peticao { background-color: rgba(0,240,255,0.20) !important; color: #7de3ff !important; }
      .tipo-decisao { background-color: rgba(255,209,102,0.20) !important; color: #ffd166 !important; }
      .tipo-contrato { background-color: rgba(57,255,136,0.20) !important; color: #39ff88 !important; }
      .tipo-prova { background-color: rgba(255,43,214,0.20) !important; color: #ff6ec7 !important; }
      .tipo-modelo { background-color: rgba(201,163,255,0.20) !important; color: #c9a3ff !important; }
      .tipo-outro { background-color: rgba(154,140,201,0.20) !important; color: #9a8cc9 !important; }

      /* ── Contatos: context menus de linha e pasta (inline #fff) ───── */
      .row-menu-dropdown, #folderCtxMenu {
        background-color: #120a24 !important;
        color: #e8f4ff !important;
        border-color: #2d1b4e !important;
      }
      .row-menu-dropdown div, .row-menu-dropdown button,
      #folderCtxMenu div, #folderCtxMenu button {
        background-color: transparent !important;
        color: #e8f4ff !important;
      }
      .row-menu-dropdown div:hover, #folderCtxMenu div:hover {
        background-color: #2d1b4e !important;
      }

      /* ── Alertas: notif cards ────────────────────────────────────── */
      .notif-card { background-color: #120a24 !important; border-color: #2d1b4e !important; color: #e8f4ff !important; }
      .notif-card .notif-title { color: #e8f4ff !important; }
      .notif-card.unread .notif-title { color: #fff !important; }
      .notif-card .notif-msg, .notif-time { color: #9a8cc9 !important; }

      /* ── Flourishes cyberpunk: glow pulsante, scanline na sidebar e glitch no título ── */
      @keyframes cyberSidebarGlow {
        0%, 100% { box-shadow: 4px 0 24px rgba(0,240,255,0.08); }
        50% { box-shadow: 4px 0 34px rgba(0,240,255,0.20), 0 0 30px rgba(255,43,214,0.08); }
      }
      @keyframes cyberButtonGlow {
        0%, 100% { box-shadow: 0 0 16px rgba(0,240,255,0.55), 0 0 2px rgba(0,240,255,0.8); }
        50% { box-shadow: 0 0 26px rgba(0,240,255,0.85), 0 0 6px rgba(255,43,214,0.65); }
      }
      @keyframes cyberLogoGlow {
        0%, 100% { filter: drop-shadow(0 0 6px rgba(0,240,255,0.45)); }
        50% { filter: drop-shadow(0 0 12px rgba(0,240,255,0.75)) drop-shadow(0 0 4px rgba(255,43,214,0.5)); }
      }
      @keyframes cyberScan {
        0% { transform: translateY(-100%); }
        100% { transform: translateY(250%); }
      }
      @keyframes cyberGlitch {
        0%, 92%, 100% { text-shadow: 0 0 10px rgba(255,43,214,0.45); transform: translate(0,0); }
        93% { text-shadow: -2px 0 rgba(0,240,255,0.85), 2px 0 rgba(255,43,214,0.85); transform: translate(-1px,0); }
        95% { text-shadow: 2px 0 rgba(0,240,255,0.85), -2px 0 rgba(255,43,214,0.85); transform: translate(1px,0); }
        96% { text-shadow: 0 0 10px rgba(255,43,214,0.45); transform: translate(0,0); }
      }
      .sidebar {
        position: relative;
        overflow: hidden;
        background: linear-gradient(180deg, #0a0014 0%, #000000 100%) !important;
        border-right: 1px solid rgba(0,240,255,0.25);
        animation: cyberSidebarGlow 4s ease-in-out infinite;
      }
      /* Feixe de "scanline" varrendo a sidebar de cima a baixo, atrás do menu */
      .sidebar-nav { position: relative; z-index: 1; }
      .sidebar::before {
        content: '';
        position: absolute;
        left: 0; right: 0;
        top: -35%;
        height: 35%;
        background: linear-gradient(180deg, transparent, rgba(0,240,255,0.14), transparent);
        animation: cyberScan 5s linear infinite;
        pointer-events: none;
        z-index: 0;
      }
      .sidebar-nav li a.active, .sidebar-nav li a:hover {
        background: rgba(0,240,255,0.08) !important;
        border-left-color: var(--color-primary) !important;
        box-shadow: inset 0 0 12px rgba(0,240,255,0.12);
        color: #fff !important;
        text-shadow: 0 0 8px rgba(0,240,255,0.6);
      }
      .header-logo img, .auth-logo img {
        animation: cyberLogoGlow 3s ease-in-out infinite;
      }
      .btn-primary {
        background: var(--color-primary) !important;
        color: #05010f !important;
        border: none !important;
        animation: cyberButtonGlow 2.4s ease-in-out infinite;
      }
      .btn-primary:hover { animation-duration: 1s; }
      .page-title {
        text-shadow: 0 0 10px rgba(255,43,214,0.45);
        animation: cyberGlitch 6s steps(1) infinite;
      }
      ::selection { background: rgba(255,43,214,0.4); color: #fff; }
      ::-webkit-scrollbar { width: 10px; height: 10px; }
      ::-webkit-scrollbar-track { background: #05010f; }
      ::-webkit-scrollbar-thumb { background: #2d1b4e; border-radius: 8px; }
      ::-webkit-scrollbar-thumb:hover { background: var(--color-primary); }
      @media (prefers-reduced-motion: reduce) {
        .sidebar, .sidebar::before, .btn-primary,
        .header-logo img, .auth-logo img, .page-title { animation: none !important; }
        .sidebar::before { display: none; }
      }
    `.trim(),
  },
  {
    id: 'synthwave',
    name: 'Synthwave',
    description: 'Retrowave roxo/rosa/laranja com brilho neon animado',
    dark: true,
    primaryColor: '#ff3ea5',
    sidebarColor: '#170a2e',
    accentColor: '#ff9e2c',
    layoutMode: 'default',
    // Dark theme completo (mesma estratégia do Escuro/Dracula/Cyberpunk) com
    // paleta retrowave (bg #170a2e, surface #241242, borda violeta #3d1f66)
    // + sidebar em gradiente e botão primário com shimmer/glow animados.
    customCss: `
      :root {
        --color-bg: #170a2e;
        --color-surface: #241242;
        --color-text: #f3e8ff;
        --color-text-muted: #a78bca;
        --color-border: #3d1f66;
        --color-primary-light: #3a0ca3;
        --hc-navy-text: var(--color-primary);
        --kanban-pendente: #ff6ec7;
        --kanban-andamento: #38bdf8;
        --kanban-concluida: #4ade80;
      }
      body { background: #170a2e !important; color: #f3e8ff !important; }
      .header { background: #241242 !important; border-bottom-color: #3d1f66 !important; }
      .header-user, .auth-subtitle { color: #a78bca !important; }
      .theme-card, .stat-card, .config-section, .form-section,
      .form-control, textarea, .preset-card,
      .table, table, table thead th, .card, .modal-content, .alert,
      div[style*="background:var(--color-surface)"] {
        background-color: #241242 !important;
        color: #f3e8ff !important;
        border-color: #3d1f66 !important;
      }
      .btn-secondary {
        background-color: transparent !important;
        color: #f3e8ff !important;
        border-color: #3d1f66 !important;
      }
      .btn-secondary:hover { background-color: #3d1f66 !important; }
      table tbody tr:hover { background-color: #170a2e !important; }
      .stat-card-value, div[style*="color:#1a56db"], div[style*="color:var(--color-primary)"] { color: var(--color-primary) !important; }
      hr, .badge-gray { border-color: #3d1f66 !important; }
      .badge-gray { background: #3d1f66 !important; color: #a78bca !important; }
      .badge-yellow, .badge-blue, .badge-purple, .badge-green, .badge-red {
        color: #f3e8ff !important;
      }
      ::placeholder { color: #7c5ba6 !important; }
      .logo-preview { background: #170a2e !important; border-color: #3d1f66 !important; }
      .logo-preview .empty { color: #a78bca !important; }

      /* ── Honorários-Contratos ─────────────────────────────────── */
      .hp-card, .ci { background-color: #241242 !important; color: #f3e8ff !important; border-color: #3d1f66 !important; }
      .hp-card .lbl, .hp-card .sub, .hp-evo-lbl, .hp-evo-val, .ci-sub, .ci-nums .cl { color: #a78bca !important; }
      .hp-meta-bar { background-color: #3d1f66 !important; }
      .badge-ok { background-color: #0f3d2e !important; color: #4ade80 !important; }
      .badge-err { background-color: #4a0e35 !important; color: #ff6ec7 !important; }
      .badge-warn { background-color: #4a2a0a !important; color: #fb923c !important; }
      .badge-muted { background-color: #3d1f66 !important; color: #c4b5fd !important; }
      .badge-proc { background-color: #2e1065 !important; color: #c4b5fd !important; }
      .filter-bar select, .filter-bar input { background-color: #241242 !important; color: #f3e8ff !important; border-color: #3d1f66 !important; }

      /* ── Processos ───────────────────────────────────────────── */
      .processo-card { background-color: #241242 !important; color: #f3e8ff !important; border-color: #3d1f66 !important; }
      .processo-card:hover { border-color: var(--color-primary) !important; box-shadow: 0 0 12px rgba(255,62,165,0.3) !important; }
      .card-divider { background-color: #3d1f66 !important; }
      .status-ativo     { background: #0f3d2e !important; color: #4ade80 !important; }
      .status-suspenso  { background: #4a2a0a !important; color: #fb923c !important; }
      .status-arquivado { background: #3d1f66 !important; color: #a78bca !important; }
      .status-encerrado { background: #4a0e35 !important; color: #ff6ec7 !important; }
      .process-preview.encontrado { background: #0f3d2e !important; border-color: #15803d !important; color: #86efac !important; }
      .process-preview.nao-encontrado { background: #4a2a0a !important; border-color: #b45309 !important; color: #fde68a !important; }
      .process-preview.loading { background: #241242 !important; border-color: #3d1f66 !important; color: #a78bca !important; }
      .search-btn, .view-btn { background-color: #241242 !important; color: #f3e8ff !important; border-color: #3d1f66 !important; }
      .search-btn:hover, .view-btn:hover { background-color: #3d1f66 !important; }
      .searchable-select-list { background-color: #241242 !important; color: #f3e8ff !important; border-color: #3d1f66 !important; }
      .searchable-select-list li:hover { background-color: #3d1f66 !important; }
      .searchable-select-list li.selected { background-color: var(--color-primary) !important; color: #fff !important; }

      /* ── Agenda ──────────────────────────────────────────────── */
      .semana-grid { background-color: #241242 !important; }
      .semana-header { background-color: #170a2e !important; color: #c4b5fd !important; border-color: #3d1f66 !important; }
      .semana-header.hoje { background-color: var(--color-primary) !important; color: #fff !important; }
      .hora-label { color: #a78bca !important; border-color: #3d1f66 !important; }
      .agenda-item { background-color: #241242 !important; color: #f3e8ff !important; border-color: #3d1f66 !important; }
      .agenda-item:active { background-color: #170a2e !important; }

      /* ── Botões precisam herdar cor (fix Alertas e outros com texto preto) ─── */
      button { color: inherit !important; }
      .filter-tab, .tab-btn, .view-btn { color: var(--color-text-muted) !important; }
      .filter-tab.active, .tab-btn.active { color: #fff !important; }
      .filter-tab:hover, .tab-btn:hover { color: var(--color-primary) !important; }
      .filter-tab .tab-count { background-color: rgba(255,255,255,0.18) !important; color: inherit !important; }
      .filter-tab.active .tab-count { background-color: rgba(255,255,255,0.28) !important; }

      /* ── Inputs/selects/textareas ────────────────────────────────── */
      input, select, textarea, button { color-scheme: dark; }
      input::placeholder, textarea::placeholder { color: #7c5ba6 !important; }
      select option { background-color: #241242 !important; color: #f3e8ff !important; }

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
        background-color: #241242 !important;
        color: #f3e8ff !important;
        border-color: #3d1f66 !important;
      }
      .modal-header, .modal-title, .modal-close { color: #f3e8ff !important; }
      .modal-overlay { background-color: rgba(10,4,20,0.78) !important; }
      .modal label, .modal .form-label { color: #a78bca !important; }

      /* ── Labels e textos secundários em formulários ──────────────── */
      label, .form-label, .lbl { color: #a78bca !important; }

      /* ── Configurações > Usuários / Feriados / Assinaturas: badges de PERFIL/STATUS ── */
      .badge-blue { background-color: #1e1b4b !important; color: #a5b4fc !important; }
      .badge-green { background-color: #0f3d2e !important; color: #4ade80 !important; }
      .badge-red   { background-color: #4a0e35 !important; color: #ff6ec7 !important; }
      .badge-yellow { background-color: #4a2a0a !important; color: #fb923c !important; }
      .badge-purple { background-color: #2e1065 !important; color: #c4b5fd !important; }

      /* ── Processos: status badges (cards) ───────────────────────── */
      .status-ativo     { background-color: #0f3d2e !important; color: #4ade80 !important; }
      .status-suspenso  { background-color: #4a2a0a !important; color: #fb923c !important; }
      .status-arquivado { background-color: rgba(167,139,202,0.18) !important; color: #a78bca !important; }
      .status-encerrado { background-color: #4a0e35 !important; color: #ff6ec7 !important; }

      /* ── Contatos: chips de tipo de documento ───────────────────── */
      .tipo-peticao { background-color: rgba(99,102,241,0.20) !important; color: #a5b4fc !important; }
      .tipo-decisao { background-color: rgba(251,191,36,0.20) !important; color: #fbbf24 !important; }
      .tipo-contrato { background-color: rgba(74,222,128,0.20) !important; color: #4ade80 !important; }
      .tipo-prova { background-color: rgba(255,110,199,0.20) !important; color: #ff6ec7 !important; }
      .tipo-modelo { background-color: rgba(196,181,253,0.20) !important; color: #c4b5fd !important; }
      .tipo-outro { background-color: rgba(167,139,202,0.20) !important; color: #a78bca !important; }

      /* ── Contatos: context menus de linha e pasta (inline #fff) ───── */
      .row-menu-dropdown, #folderCtxMenu {
        background-color: #241242 !important;
        color: #f3e8ff !important;
        border-color: #3d1f66 !important;
      }
      .row-menu-dropdown div, .row-menu-dropdown button,
      #folderCtxMenu div, #folderCtxMenu button {
        background-color: transparent !important;
        color: #f3e8ff !important;
      }
      .row-menu-dropdown div:hover, #folderCtxMenu div:hover {
        background-color: #3d1f66 !important;
      }

      /* ── Alertas: notif cards ────────────────────────────────────── */
      .notif-card { background-color: #241242 !important; border-color: #3d1f66 !important; color: #f3e8ff !important; }
      .notif-card .notif-title { color: #f3e8ff !important; }
      .notif-card.unread .notif-title { color: #fff !important; }
      .notif-card .notif-msg, .notif-time { color: #a78bca !important; }

      /* ── Flourishes synthwave: sidebar em gradiente + glow no botão/logo/título ── */
      @keyframes synthShift { 0% { background-position: 0% 50%; } 100% { background-position: 200% 50%; } }
      @keyframes synthGlow {
        0%, 100% { box-shadow: 0 0 14px rgba(255,62,165,0.45), 0 0 2px rgba(255,158,44,0.6); }
        50% { box-shadow: 0 0 22px rgba(255,62,165,0.7), 0 0 4px rgba(255,158,44,0.85); }
      }
      .sidebar {
        background: linear-gradient(180deg, #170a2e 0%, #2e1065 45%, #170a2e 100%) !important;
        border-right: 1px solid rgba(255,62,165,0.25);
      }
      .sidebar-nav li a.active, .sidebar-nav li a:hover {
        background: rgba(255,62,165,0.10) !important;
        border-left-color: var(--color-primary) !important;
        color: #fff !important;
      }
      .header-logo img, .auth-logo img { filter: drop-shadow(0 0 6px rgba(255,62,165,0.5)); }
      .btn-primary {
        background: linear-gradient(90deg, var(--color-primary), var(--color-accent), var(--color-primary)) !important;
        background-size: 200% 100%;
        animation: synthShift 4s linear infinite, synthGlow 2.4s ease-in-out infinite;
        border: none !important;
      }
      .page-title { text-shadow: 0 0 12px rgba(255,158,44,0.5); }
      ::selection { background: rgba(255,62,165,0.4); color: #fff; }
      ::-webkit-scrollbar { width: 10px; height: 10px; }
      ::-webkit-scrollbar-track { background: #170a2e; }
      ::-webkit-scrollbar-thumb { background: #3d1f66; border-radius: 8px; }
      ::-webkit-scrollbar-thumb:hover { background: var(--color-primary); }
      @media (prefers-reduced-motion: reduce) {
        .btn-primary { animation: none !important; }
      }
    `.trim(),
  },
  {
    id: 'matrix',
    name: 'Matrix',
    description: 'Terminal hacker verde-sobre-preto com cursor piscando',
    dark: true,
    primaryColor: '#00ff8c',
    sidebarColor: '#000000',
    accentColor: '#39ff14',
    layoutMode: 'default',
    // Dark theme completo (mesma estratégia do Escuro/Dracula/Cyberpunk),
    // paleta monocromática verde-sobre-preto com pitadas de vermelho/âmbar
    // só para status de erro/aviso. --color-primary é verde muito claro:
    // onde o preset usa background-color: var(--color-primary) o texto é
    // escurecido para manter contraste, como no Cyberpunk.
    customCss: `
      :root {
        --color-bg: #000000;
        --color-surface: #0a0f0a;
        --color-text: #d5ffd8;
        --color-text-muted: #5a9a60;
        --color-border: #154019;
        --color-primary-light: #0a3d16;
        --hc-navy-text: var(--color-primary);
        --kanban-pendente: #ffd24d;
        --kanban-andamento: #5eead4;
        --kanban-concluida: #39ff14;
      }
      body { background: #000000 !important; color: #d5ffd8 !important; }
      .header { background: #0a0f0a !important; border-bottom-color: #154019 !important; }
      .header-user, .auth-subtitle { color: #5a9a60 !important; }
      .theme-card, .stat-card, .config-section, .form-section,
      .form-control, textarea, .preset-card,
      .table, table, table thead th, .card, .modal-content, .alert,
      div[style*="background:var(--color-surface)"] {
        background-color: #0a0f0a !important;
        color: #d5ffd8 !important;
        border-color: #154019 !important;
      }
      .btn-secondary {
        background-color: transparent !important;
        color: #d5ffd8 !important;
        border-color: #154019 !important;
      }
      .btn-secondary:hover { background-color: #154019 !important; }
      table tbody tr:hover { background-color: #000000 !important; }
      .stat-card-value, div[style*="color:#1a56db"], div[style*="color:var(--color-primary)"] { color: var(--color-primary) !important; }
      hr, .badge-gray { border-color: #154019 !important; }
      .badge-gray { background: #154019 !important; color: #5a9a60 !important; }
      .badge-yellow, .badge-blue, .badge-purple, .badge-green, .badge-red {
        color: #eafff2 !important;
      }
      ::placeholder { color: #3f6b44 !important; }
      .logo-preview { background: #000000 !important; border-color: #154019 !important; }
      .logo-preview .empty { color: #5a9a60 !important; }

      /* ── Honorários-Contratos ─────────────────────────────────── */
      .hp-card, .ci { background-color: #0a0f0a !important; color: #d5ffd8 !important; border-color: #154019 !important; }
      .hp-card .lbl, .hp-card .sub, .hp-evo-lbl, .hp-evo-val, .ci-sub, .ci-nums .cl { color: #5a9a60 !important; }
      .hp-meta-bar { background-color: #154019 !important; }
      .badge-ok { background-color: #0a3d16 !important; color: #39ff14 !important; }
      .badge-err { background-color: #3d0a0a !important; color: #ff4d4d !important; }
      .badge-warn { background-color: #3d2d0a !important; color: #ffd24d !important; }
      .badge-muted { background-color: #154019 !important; color: #8fd696 !important; }
      .badge-proc { background-color: #0a3d33 !important; color: #5eead4 !important; }
      .filter-bar select, .filter-bar input { background-color: #0a0f0a !important; color: #d5ffd8 !important; border-color: #154019 !important; }

      /* ── Processos ───────────────────────────────────────────── */
      .processo-card { background-color: #0a0f0a !important; color: #d5ffd8 !important; border-color: #154019 !important; }
      .processo-card:hover { border-color: var(--color-primary) !important; box-shadow: 0 0 12px rgba(0,255,140,0.35) !important; }
      .card-divider { background-color: #154019 !important; }
      .status-ativo     { background: #0a3d16 !important; color: #39ff14 !important; }
      .status-suspenso  { background: #3d2d0a !important; color: #ffd24d !important; }
      .status-arquivado { background: #154019 !important; color: #8fd696 !important; }
      .status-encerrado { background: #3d0a0a !important; color: #ff4d4d !important; }
      .process-preview.encontrado { background: #0a3d16 !important; border-color: #15803d !important; color: #86efac !important; }
      .process-preview.nao-encontrado { background: #3d2d0a !important; border-color: #b45309 !important; color: #fde68a !important; }
      .process-preview.loading { background: #0a0f0a !important; border-color: #154019 !important; color: #5a9a60 !important; }
      .search-btn, .view-btn { background-color: #0a0f0a !important; color: #d5ffd8 !important; border-color: #154019 !important; }
      .search-btn:hover, .view-btn:hover { background-color: #154019 !important; }
      .searchable-select-list { background-color: #0a0f0a !important; color: #d5ffd8 !important; border-color: #154019 !important; }
      .searchable-select-list li:hover { background-color: #154019 !important; }
      .searchable-select-list li.selected { background-color: var(--color-primary) !important; color: #04140a !important; }

      /* ── Agenda ──────────────────────────────────────────────── */
      .semana-grid { background-color: #0a0f0a !important; }
      .semana-header { background-color: #000000 !important; color: #8fd696 !important; border-color: #154019 !important; }
      .semana-header.hoje { background-color: var(--color-primary) !important; color: #04140a !important; }
      .hora-label { color: #5a9a60 !important; border-color: #154019 !important; }
      .agenda-item { background-color: #0a0f0a !important; color: #d5ffd8 !important; border-color: #154019 !important; }
      .agenda-item:active { background-color: #000000 !important; }

      /* ── Botões precisam herdar cor (fix Alertas e outros com texto preto) ─── */
      button { color: inherit !important; }
      .filter-tab, .tab-btn, .view-btn { color: var(--color-text-muted) !important; }
      .filter-tab.active, .tab-btn.active { color: #fff !important; }
      .filter-tab:hover, .tab-btn:hover { color: var(--color-primary) !important; }
      .filter-tab .tab-count { background-color: rgba(255,255,255,0.18) !important; color: inherit !important; }
      .filter-tab.active .tab-count { background-color: rgba(255,255,255,0.28) !important; }

      /* ── Inputs/selects/textareas ────────────────────────────────── */
      input, select, textarea, button { color-scheme: dark; }
      input::placeholder, textarea::placeholder { color: #3f6b44 !important; }
      select option { background-color: #0a0f0a !important; color: #d5ffd8 !important; }

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
        background-color: #0a0f0a !important;
        color: #d5ffd8 !important;
        border-color: #154019 !important;
      }
      .modal-header, .modal-title, .modal-close { color: #d5ffd8 !important; }
      .modal-overlay { background-color: rgba(0,0,0,0.85) !important; }
      .modal label, .modal .form-label { color: #5a9a60 !important; }

      /* ── Labels e textos secundários em formulários ──────────────── */
      label, .form-label, .lbl { color: #5a9a60 !important; }

      /* ── Configurações > Usuários / Feriados / Assinaturas: badges de PERFIL/STATUS ── */
      .badge-blue { background-color: #0a3d33 !important; color: #5eead4 !important; }
      .badge-green { background-color: #0a3d16 !important; color: #39ff14 !important; }
      .badge-red   { background-color: #3d0a0a !important; color: #ff4d4d !important; }
      .badge-yellow { background-color: #3d2d0a !important; color: #ffd24d !important; }
      .badge-purple { background-color: #0a3d33 !important; color: #5eead4 !important; }

      /* ── Processos: status badges (cards) ───────────────────────── */
      .status-ativo     { background-color: #0a3d16 !important; color: #39ff14 !important; }
      .status-suspenso  { background-color: #3d2d0a !important; color: #ffd24d !important; }
      .status-arquivado { background-color: rgba(90,154,96,0.18) !important; color: #5a9a60 !important; }
      .status-encerrado { background-color: #3d0a0a !important; color: #ff4d4d !important; }

      /* ── Contatos: chips de tipo de documento ───────────────────── */
      .tipo-peticao { background-color: rgba(94,234,212,0.20) !important; color: #5eead4 !important; }
      .tipo-decisao { background-color: rgba(255,210,77,0.20) !important; color: #ffd24d !important; }
      .tipo-contrato { background-color: rgba(57,255,20,0.20) !important; color: #39ff14 !important; }
      .tipo-prova { background-color: rgba(255,77,77,0.20) !important; color: #ff4d4d !important; }
      .tipo-modelo { background-color: rgba(94,234,212,0.20) !important; color: #5eead4 !important; }
      .tipo-outro { background-color: rgba(90,154,96,0.20) !important; color: #5a9a60 !important; }

      /* ── Contatos: context menus de linha e pasta (inline #fff) ───── */
      .row-menu-dropdown, #folderCtxMenu {
        background-color: #0a0f0a !important;
        color: #d5ffd8 !important;
        border-color: #154019 !important;
      }
      .row-menu-dropdown div, .row-menu-dropdown button,
      #folderCtxMenu div, #folderCtxMenu button {
        background-color: transparent !important;
        color: #d5ffd8 !important;
      }
      .row-menu-dropdown div:hover, #folderCtxMenu div:hover {
        background-color: #154019 !important;
      }

      /* ── Alertas: notif cards ────────────────────────────────────── */
      .notif-card { background-color: #0a0f0a !important; border-color: #154019 !important; color: #d5ffd8 !important; }
      .notif-card .notif-title { color: #d5ffd8 !important; }
      .notif-card.unread .notif-title { color: #fff !important; }
      .notif-card .notif-msg, .notif-time { color: #5a9a60 !important; }

      /* ── Flourishes matrix: flicker de CRT, glow verde e cursor piscando ── */
      @keyframes crtFlicker { 0%, 96%, 100% { opacity: 1; } 97% { opacity: 0.96; } 98% { opacity: 1; } }
      @keyframes blinkCursor { 0%, 49% { opacity: 1; } 50%, 100% { opacity: 0; } }
      @keyframes matrixGlow {
        0%, 100% { text-shadow: 0 0 6px rgba(0,255,140,0.5); }
        50% { text-shadow: 0 0 14px rgba(0,255,140,0.85); }
      }
      body { animation: crtFlicker 9s steps(1) infinite; }
      .page-title { font-family: 'Courier New', monospace; letter-spacing: 0.5px; animation: matrixGlow 2.4s ease-in-out infinite; }
      .page-title::after { content: '_'; animation: blinkCursor 1s steps(1) infinite; margin-left: 2px; }
      .sidebar-nav li a.active, .sidebar-nav li a:hover {
        background: rgba(0,255,140,0.08) !important;
        border-left-color: var(--color-primary) !important;
        color: #fff !important;
        text-shadow: 0 0 8px rgba(0,255,140,0.7);
      }
      .btn-primary {
        background: var(--color-primary) !important;
        color: #04140a !important;
        box-shadow: 0 0 14px rgba(0,255,140,0.5);
        border: none !important;
      }
      .btn-primary:hover { box-shadow: 0 0 22px rgba(0,255,140,0.75); }
      ::selection { background: rgba(0,255,140,0.35); color: #04140a; }
      ::-webkit-scrollbar { width: 10px; height: 10px; }
      ::-webkit-scrollbar-track { background: #000000; }
      ::-webkit-scrollbar-thumb { background: #154019; border-radius: 8px; }
      ::-webkit-scrollbar-thumb:hover { background: var(--color-primary); }
      @media (prefers-reduced-motion: reduce) {
        body, .page-title, .page-title::after,
        .sidebar-nav li a.active, .sidebar-nav li a:hover { animation: none !important; }
      }
    `.trim(),
  },
  {
    id: 'ocean',
    name: 'Ocean',
    description: 'Azul profundo e turquesa',
    dark: false,
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
    dark: false,
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
    dark: false,
    primaryColor: '#16a34a',
    sidebarColor: '#14532d',
    accentColor: '#84cc16',
    layoutMode: 'default',
    customCss: '',
  },
  {
    id: 'aurora',
    name: 'Aurora',
    description: 'Gradiente vibrante inspirado na aurora boreal, em movimento',
    dark: false,
    primaryColor: '#a855f7',
    sidebarColor: '#1e1b4b',
    accentColor: '#34d399',
    layoutMode: 'default',
    // Reskin "claro" (mantém --color-bg/--color-surface padrão): sidebar e
    // botão primário com gradiente animado (roxo/ciano/verde), título com
    // texto em gradiente cíclico. Respeita prefers-reduced-motion.
    customCss: `
      @keyframes auroraShift {
        0% { background-position: 0% 50%; }
        50% { background-position: 100% 50%; }
        100% { background-position: 0% 50%; }
      }
      .sidebar {
        background: linear-gradient(120deg, #4c1d95, #7c3aed, #0891b2, #059669, #7c3aed) !important;
        background-size: 400% 400%;
        animation: auroraShift 12s ease infinite;
        border-right: none;
      }
      .sidebar-nav li a {
        border-left: none !important;
        border-radius: 8px;
        margin: 2px 8px;
        width: calc(100% - 16px);
        transition: background .2s, transform .15s;
      }
      .sidebar-nav li a:hover { transform: translateX(3px); }
      .sidebar-nav li a.active, .sidebar-nav li a:hover {
        background: rgba(255,255,255,0.16) !important;
        color: #fff !important;
        box-shadow: 0 0 16px rgba(255,255,255,0.25);
      }
      .btn-primary {
        background: linear-gradient(120deg, #a855f7, #22d3ee, #34d399, #a855f7) !important;
        background-size: 300% 300%;
        animation: auroraShift 6s ease infinite;
        border: none;
        color: #fff;
      }
      .page-title {
        background: linear-gradient(120deg, #a855f7, #22d3ee, #34d399, #ec4899, #a855f7);
        background-size: 300% 300%;
        animation: auroraShift 8s ease infinite;
        -webkit-background-clip: text;
        background-clip: text;
        -webkit-text-fill-color: transparent;
        color: transparent;
      }
      .stat-card, .card, .config-section, .theme-card {
        border-radius: 14px;
        transition: transform .2s, box-shadow .2s;
      }
      .stat-card:hover { transform: translateY(-3px); box-shadow: 0 14px 32px rgba(124,58,237,0.18); }
      .badge { border-radius: 999px; }
      @media (prefers-reduced-motion: reduce) {
        .sidebar, .btn-primary, .page-title { animation: none !important; }
      }
    `.trim(),
  },
  {
    id: 'tropical',
    name: 'Tropical',
    description: 'Coral e turquesa com sidebar em ondas e cards flutuantes',
    dark: false,
    primaryColor: '#fb7185',
    sidebarColor: '#134e4a',
    accentColor: '#2dd4bf',
    layoutMode: 'default',
    // Reskin "claro": sidebar em gradiente lento (efeito onda), cards mais
    // arredondados com leve "flutuação" no hover, indicador de aba em
    // gradiente e glow pulsante nos cards de notificação não lidos.
    customCss: `
      @keyframes waveShift {
        0% { background-position: 0% 50%; }
        100% { background-position: 200% 50%; }
      }
      @keyframes unreadPulse {
        0%, 100% { box-shadow: 0 0 0 rgba(251,113,133,0); }
        50% { box-shadow: 0 0 0 5px rgba(251,113,133,0.16); }
      }
      .sidebar {
        background: linear-gradient(120deg, var(--color-sidebar), #0f766e, #134e4a) !important;
        background-size: 200% 200%;
        animation: waveShift 10s linear infinite;
      }
      .sidebar-nav li a {
        border-left: none !important;
        border-radius: 10px 999px 999px 10px;
        margin: 2px 8px 2px 0;
        transition: transform .15s ease, background .15s;
      }
      .sidebar-nav li a:hover { transform: translateX(4px); }
      .sidebar-nav li a.active, .sidebar-nav li a:hover {
        background: rgba(255,255,255,0.14) !important;
        color: #fff !important;
      }
      .btn-primary {
        background: linear-gradient(135deg, var(--color-primary), var(--color-accent));
        border: none;
        transition: transform .15s ease;
      }
      .btn-primary:active { transform: scale(0.94); }
      .stat-card, .card, .config-section, .theme-card, .modal, .preset-card { border-radius: 18px; }
      .stat-card { transition: transform .2s ease, box-shadow .2s ease; }
      .stat-card:hover { transform: translateY(-3px) rotate(-0.3deg); box-shadow: 0 14px 30px rgba(251,113,133,0.18); }
      .badge { border-radius: 999px; }
      .tab-btn.active { position: relative; }
      .tab-btn.active::after {
        content: '';
        position: absolute;
        left: 16px; right: 16px; bottom: -2px;
        height: 3px;
        border-radius: 3px;
        background: linear-gradient(90deg, var(--color-primary), var(--color-accent));
      }
      .notif-card.unread { animation: unreadPulse 2s ease-in-out infinite; }
      @media (prefers-reduced-motion: reduce) {
        .sidebar, .stat-card, .notif-card.unread { animation: none !important; }
      }
    `.trim(),
  },
  {
    id: 'pop',
    name: 'Pop',
    description: 'Neo-pop ousado: contornos grossos, sombras duras e sacudida',
    dark: false,
    primaryColor: '#eab308',
    sidebarColor: '#1c1917',
    accentColor: '#ec4899',
    layoutMode: 'default',
    // Visual "neo-brutalista" (bordas grossas + sombra sólida deslocada em
    // vez de blur), botão com shimmer contínuo e badges que "balançam" no
    // hover. Contraste do texto do btn-primary é escurecido pois a cor
    // primária (amarelo) é muito clara para texto branco.
    customCss: `
      @keyframes shimmerSweep {
        0% { background-position: -200% 0; }
        100% { background-position: 200% 0; }
      }
      @keyframes wiggle {
        0%, 100% { transform: rotate(0deg); }
        25% { transform: rotate(-4deg); }
        75% { transform: rotate(4deg); }
      }
      .card, .stat-card, .config-section, .theme-card, .modal, .modal-content, .preset-card {
        border: 2px solid var(--color-text) !important;
        border-radius: 12px;
        box-shadow: 4px 4px 0 var(--color-primary) !important;
        transition: transform .15s ease, box-shadow .15s ease;
      }
      .stat-card:hover, .preset-card:hover {
        transform: translate(-2px,-2px);
        box-shadow: 6px 6px 0 var(--color-primary) !important;
      }
      .btn-primary {
        background: linear-gradient(100deg, var(--color-primary) 40%, rgba(255,255,255,0.55) 50%, var(--color-primary) 60%) !important;
        background-size: 250% 100%;
        animation: shimmerSweep 3s linear infinite;
        border: 2px solid var(--color-text);
        color: #1c1917 !important;
        font-weight: 700;
        box-shadow: 3px 3px 0 var(--color-text);
      }
      .btn-primary:hover { animation-duration: 1s; }
      .badge { border-radius: 6px; border: 1.5px solid currentColor; }
      .badge:hover { animation: wiggle .4s ease; }
      .sidebar { border-right: 2px solid var(--color-accent); }
      .sidebar-nav li a.active, .sidebar-nav li a:hover { border-left-color: var(--color-accent) !important; }
      @media (prefers-reduced-motion: reduce) {
        .btn-primary, .badge:hover { animation: none !important; }
      }
    `.trim(),
  },
  {
    id: 'compact',
    name: 'Compacto',
    description: 'Densidade maior de informação na tela',
    dark: false,
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