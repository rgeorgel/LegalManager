// Conteúdo dos tutoriais guiados (product tour). Fica separado de tour.js
// (o motor) para permitir editar textos/passos sem tocar em lógica.
//
// TourStep = {
//   id, page, selector,           // page: caminho absoluto; selector: CSS, ex: '[data-tour="processos-novo"]'
//   title, text,                   // text pode conter HTML simples (conteúdo nosso, não de usuário)
//   placement?,                    // 'top'|'bottom'|'left'|'right'|'auto' (default 'auto')
//   planRequired?,                 // 'Plus'|'Pro' — omitido = visível a todos os planos
//   spotlightPadding?,
//   onBeforeShow?,                 // () => void|Promise — ex: trocar de aba antes de destacar
// }
// Tour = {
//   id, label, steps: TourStep[],
//   onComplete?,                    // () => void — roda só quando o tour termina de
//                                    // verdade (avançou pelo último passo, não ao
//                                    // pular/fechar no meio); use para desfazer efeitos
//                                    // colaterais da demonstração (fechar um modal
//                                    // aberto, excluir um registro criado só pra
//                                    // mostrar algo preenchido).
// }

export const TOURS = [
  {
    id: 'geral',
    label: '👋 Tour geral',
    steps: [
      {
        id: 'dashboard-stats',
        page: '/pages/dashboard.html',
        selector: '[data-tour="dashboard-stats"]',
        title: 'Bem-vindo(a) à Causify!',
        text: 'Aqui você acompanha os números do escritório: processos ativos, contatos, tarefas em aberto e mais, tudo em um só lugar.',
        placement: 'bottom',
      },
      {
        id: 'dashboard-financeiro',
        page: '/pages/dashboard.html',
        selector: '[data-tour="dashboard-financeiro"]',
        title: 'Financeiro do escritório',
        text: 'Receitas, despesas e saldo do mês, atualizados automaticamente.',
        placement: 'top',
        planRequired: 'Plus',
      },
      {
        id: 'processos-novo',
        page: '/pages/processos.html',
        selector: '#btnNovo',
        title: 'Cadastre processos',
        text: 'Adicione um processo manualmente ou importe automaticamente pelo número CNJ.',
        placement: 'bottom',
      },
      {
        id: 'processos-filtros',
        page: '/pages/processos.html',
        selector: '[data-tour="processos-filtros"]',
        title: 'Encontre rápido',
        text: 'Filtre por status, área do direito ou busque por número, tribunal ou comarca.',
        placement: 'bottom',
      },
      {
        id: 'tarefas-nova',
        page: '/pages/tarefas.html',
        selector: '#btnNova',
        title: 'Tarefas e prazos',
        text: 'Crie tarefas e prazos, vinculados ou não a um processo ou contato.',
        placement: 'bottom',
      },
      {
        id: 'tarefas-kanban',
        page: '/pages/tarefas.html',
        selector: '#tabBtnKanban',
        title: 'Quadro Kanban',
        text: 'No plano Plus, arraste tarefas entre as colunas Pendente, Em Andamento e Concluída.',
        placement: 'bottom',
        planRequired: 'Plus',
      },
      {
        id: 'tarefas-widget-flutuante',
        page: '/pages/tarefas.html',
        selector: '#tarefasWidget .tw-toggle',
        title: 'Tarefas em qualquer tela',
        text: 'A partir do plano Plus, este menu flutuante fica disponível na lateral direita em todas as páginas do sistema — veja e avance suas tarefas pendentes sem precisar abrir esta tela.',
        placement: 'left',
        planRequired: 'Plus',
      },
    ],
  },
  {
    id: 'processos',
    label: '⚖️ Processos',
    steps: [
      {
        id: 'processos-novo-2',
        page: '/pages/processos.html',
        selector: '#btnNovo',
        title: 'Novo processo',
        text: 'Cadastre manualmente ou importe pelo número CNJ — os dados do tribunal são preenchidos automaticamente quando disponíveis.',
        placement: 'bottom',
      },
      {
        id: 'processos-importar-oab',
        page: '/pages/processos.html',
        selector: '#btnImportarOab',
        title: 'Importe pela sua OAB',
        text: 'Busque e importe de uma vez todos os processos vinculados ao seu número de OAB.',
        placement: 'bottom',
      },
      {
        id: 'processos-filtros-2',
        page: '/pages/processos.html',
        selector: '[data-tour="processos-filtros"]',
        title: 'Filtros',
        text: 'Combine status, área do direito e busca livre para localizar processos rapidamente.',
        placement: 'bottom',
      },
      {
        id: 'processos-visualizacao',
        page: '/pages/processos.html',
        selector: '[data-tour="processos-visualizacao"]',
        title: 'Grade ou lista',
        text: 'Alterne entre visualização em grade (cartões) ou em lista, conforme sua preferência.',
        placement: 'left',
      },
    ],
  },
  {
    id: 'tarefas',
    label: '✅ Tarefas',
    steps: [
      {
        id: 'tarefas-nova-2',
        page: '/pages/tarefas.html',
        selector: '#btnNova',
        title: 'Nova tarefa',
        text: 'Crie tarefas com prioridade, prazo e responsável — vincule a um processo ou contato se quiser.',
        placement: 'bottom',
      },
      {
        id: 'tarefas-filtros',
        page: '/pages/tarefas.html',
        selector: '[data-tour="tarefas-filtros"]',
        title: 'Filtre suas tarefas',
        text: 'Filtre por status, prioridade, tipo (tarefa ou prazo) ou apenas as atrasadas.',
        placement: 'bottom',
      },
      {
        id: 'tarefas-kanban-2',
        page: '/pages/tarefas.html',
        selector: '#tabBtnKanban',
        title: 'Quadro Kanban',
        text: 'No plano Plus, visualize e mova tarefas entre colunas por status, arrastando os cartões.',
        placement: 'bottom',
        planRequired: 'Plus',
      },
      {
        id: 'tarefas-kanban-board',
        page: '/pages/tarefas.html',
        selector: '#board',
        title: 'Arraste entre colunas',
        text: 'Arraste um cartão para mudar o status da tarefa — a mudança é salva automaticamente.',
        placement: 'top',
        planRequired: 'Plus',
        onBeforeShow: () => { document.getElementById('tabBtnKanban')?.click(); },
      },
      {
        id: 'tarefas-widget-flutuante-2',
        page: '/pages/tarefas.html',
        selector: '#tarefasWidget .tw-toggle',
        title: 'Tarefas em qualquer tela',
        text: 'A partir do plano Plus, este menu flutuante fica disponível na lateral direita em todas as páginas do sistema — veja e avance suas tarefas pendentes sem precisar abrir esta tela.',
        placement: 'left',
        planRequired: 'Plus',
      },
    ],
  },
  {
    id: 'documentos',
    label: '📁 Documentos',
    steps: [
      {
        id: 'documentos-upload',
        page: '/pages/documentos.html',
        selector: '#btnUpload',
        title: 'Envie documentos',
        text: 'Faça upload de arquivos e organize-os por processo, contato ou pasta livre.',
        placement: 'bottom',
      },
      {
        id: 'documentos-pastas',
        page: '/pages/documentos.html',
        selector: '[data-tour="documentos-pastas"]',
        title: 'Organize em pastas',
        text: 'Crie pastas para manter os documentos do escritório organizados.',
        placement: 'right',
      },
      {
        id: 'documentos-modelos',
        page: '/pages/documentos.html',
        selector: '#tabBtnModelos',
        title: 'Modelos de documentos',
        text: 'No plano Pro, crie modelos reutilizáveis e gere peças automaticamente com IA.',
        placement: 'bottom',
        planRequired: 'Pro',
      },
    ],
  },
  {
    // Calculadora avulsa de honorários (honorarios.html) — assunto parecido
    // com o tour "honorarios-contratos", mas telas diferentes: não misturar
    // os dois num tour só.
    id: 'honorarios',
    label: '💰 Honorários (calculadora)',
    steps: [
      {
        id: 'honorarios-calc-modo',
        page: '/pages/honorarios.html',
        selector: '[data-tour="honorarios-calc-modo"]',
        title: 'Calculadora de honorários',
        text: 'Calcule honorários judiciais (tabela OAB) ou extrajudiciais.',
        placement: 'bottom',
      },
      {
        id: 'honorarios-calc-valor',
        page: '/pages/honorarios.html',
        selector: '#hc-iv',
        title: 'Informe o valor da causa',
        text: 'Escolha a área e o tipo de ação, informe o valor da causa e o nome do cliente. Preenchemos um exemplo para você ver funcionando.',
        placement: 'right',
        // Preenche a área, o tipo de ação, o valor e o cliente com um
        // exemplo, disparando os mesmos handlers dos campos (globais em
        // honorarios.html, fora de módulo), pra a tela já mostrar o
        // cálculo pronto quando o passo aparecer.
        onBeforeShow: () => {
          const sa = document.getElementById('hc-sa');
          if (sa) { sa.value = 'Cível'; window.hcOnArea?.(); }
          const sc = document.getElementById('hc-sc');
          if (sc) { sc.value = '0'; window.hcOnAct?.(); }
          const iv = document.getElementById('hc-iv');
          if (iv) { iv.value = '50000'; window.hcOnVal?.(); }
          const cliente = document.getElementById('hc-cliente');
          if (cliente) { cliente.value = 'Maria Silva'; window.hcRender?.(); }
        },
      },
      {
        id: 'honorarios-resultados',
        page: '/pages/honorarios.html',
        selector: '#hc-rp2',
        title: 'Resultado do cálculo',
        text: 'À direita, você vê o honorário base pela tabela da OAB, o honorário sugerido já com os ajustes (complexidade, risco, urgência etc.) e o cronograma de pagamento das parcelas.',
        placement: 'left',
      },
      {
        id: 'honorarios-historico-salvar',
        page: '/pages/honorarios.html',
        selector: '[data-tour="honorarios-btn-historico"]',
        title: 'Salve no histórico',
        text: 'Clique em "+ Histórico" para guardar este cálculo e consultá-lo depois.',
        placement: 'top',
        // Só destaca o botão aqui — NÃO clica ainda. hcSaveHist() troca o
        // texto/estado do botão (e o resultado é re-renderizado depois de
        // salvar), então clicar neste passo faz o botão mudar embaixo do
        // realce já posicionado, deixando-o desalinhado. O clique de
        // verdade acontece no próximo passo, onde já esperamos ele
        // terminar antes de medir/destacar qualquer coisa.
      },
      {
        id: 'honorarios-historico-ver',
        page: '/pages/honorarios.html',
        selector: '.hc-hist-grid',
        title: 'Cálculo salvo',
        text: 'Na aba Histórico ficam todos os cálculos salvos, com data e detalhes — é só abrir a aba para consultar.',
        placement: 'bottom',
        // Espera o salvamento (hcSaveHist é assíncrono — chama a API) E o
        // recarregamento da lista terminarem de verdade antes de seguir,
        // em vez de só clicar e torcer pelo tempo: clicar na aba sozinho
        // troca o painel na hora, mas o carregamento em si (hcLoadHist)
        // é assíncrono, e medir/destacar a grade antes dele terminar
        // pegava o card ainda "achatado" (sem conteúdo final).
        onBeforeShow: async () => {
          const btn = document.querySelector('[data-tour="honorarios-btn-historico"]');
          if (btn && !btn.disabled && window.hcSaveHist) {
            try { await window.hcSaveHist(btn); } catch { /* segue mesmo se falhar */ }
          }
          document.querySelectorAll('.hc-tab')[1]?.click();
          if (window.hcLoadHist && window.hcRenderHist) {
            try { await window.hcLoadHist(); window.hcRenderHist(); } catch {}
          }
        },
      },
      {
        id: 'honorarios-proposta-btn',
        page: '/pages/honorarios.html',
        selector: '[data-tour="honorarios-btn-proposta"]',
        title: 'Envie como proposta',
        text: 'Clique em "+ Adicionar à Proposta" para transformar o cálculo num documento pronto para o cliente.',
        placement: 'top',
        onBeforeShow: () => { document.querySelectorAll('.hc-tab')[0]?.click(); },
      },
      {
        id: 'honorarios-proposta-ver',
        page: '/pages/honorarios.html',
        selector: '#hc-pane-proposta .hc-act-row',
        title: 'Proposta pronta',
        text: 'O texto da proposta é gerado automaticamente a partir do cálculo. Clique em "Imprimir / Salvar PDF" para gerar um PDF pronto para enviar ao cliente, ou copie o texto.',
        placement: 'bottom',
        onBeforeShow: () => { document.querySelector('[data-tour="honorarios-btn-proposta"]')?.click(); },
      },
      {
        id: 'honorarios-config',
        page: '/pages/honorarios.html',
        selector: '#hc-pane-cfg',
        title: 'Configurações da calculadora',
        text: 'Defina o percentual do adicional de especialidade (aplicado quando "Especialista" está ativo) e acompanhe os índices de correção monetária: IPCA, IGP-M e Tabela TJSP.',
        placement: 'auto',
        onBeforeShow: () => { document.querySelectorAll('.hc-tab')[4]?.click(); },
      },
    ],
    // O passo "honorarios-historico-ver" salva de verdade um cálculo de
    // exemplo no histórico do usuário (para ele aparecer na aba). Ao
    // concluir o tour, apaga esse registro — reaproveita o botão
    // "Excluir" do próprio card mais recente (sem confirm(), já chama a
    // API de exclusão), então não precisa saber o id nem duplicar lógica.
    onComplete: () => {
      const primeiroCard = document.querySelector('.hc-hist-grid .hc-hcard');
      primeiroCard?.querySelector('.hc-btns button:last-child')?.click();
    },
  },
  {
    // Módulo de contratos de honorários (honorarios-contratos.html) — tela
    // separada da calculadora acima, por isso é um tour à parte.
    id: 'honorarios-contratos',
    label: '💼 Honorários Contratos',
    steps: [
      {
        id: 'honorarios-contrato-novo',
        page: '/pages/honorarios-contratos.html',
        selector: 'a[href="/pages/honorarios-contrato-novo.html"]',
        title: 'Contratos de honorários',
        text: 'No plano Plus, cadastre contratos com parcelas, vencimentos e cobrança recorrente.',
        placement: 'bottom',
        planRequired: 'Plus',
      },
      {
        id: 'honorarios-contrato-dashboard',
        page: '/pages/honorarios-contratos.html',
        selector: '#dashTiles',
        title: 'Acompanhe os recebimentos',
        text: 'Veja evolução de pagamentos, meta mensal e clientes em atraso.',
        placement: 'bottom',
        planRequired: 'Plus',
      },
    ],
  },
  {
    id: 'prazos',
    label: '📅 Cálculo de Prazos',
    steps: [
      {
        id: 'prazos-form',
        page: '/pages/calculadora-prazos.html',
        selector: '[data-tour="prazos-form"]',
        title: 'Calculadora de prazos',
        text: 'Informe a data inicial, a quantidade de dias e o tribunal — feriados nacionais, estaduais e municipais entram automaticamente na conta. Preenchemos 45 dias a partir de hoje como exemplo.',
        placement: 'right',
        planRequired: 'Plus',
        // Preenche data de início (hoje) e quantidade de dias — hcOnArea-
        // style: os campos são lidos direto do DOM quando o usuário clica
        // em "Calcular", então só setar o .value já basta.
        onBeforeShow: () => {
          const inicio = document.getElementById('cpInicio');
          if (inicio) inicio.value = new Date().toISOString().slice(0, 10);
          const dias = document.getElementById('cpDias');
          if (dias) dias.value = '45';
        },
      },
      {
        id: 'prazos-btn-calcular',
        page: '/pages/calculadora-prazos.html',
        selector: '#cpBtnCalcular',
        title: 'Calcule o prazo',
        text: 'Clique em "Calcular prazo" para ver a data final, já considerando dias úteis ou corridos.',
        placement: 'right',
        planRequired: 'Plus',
      },
      {
        id: 'prazos-resultado',
        page: '/pages/calculadora-prazos.html',
        selector: '#cpDataFinal',
        title: 'Resultado do cálculo',
        text: 'Esta é a data final do prazo, já com o dia da semana por extenso ao lado.',
        placement: 'left',
        planRequired: 'Plus',
        // O clique de verdade acontece só agora (não no passo anterior,
        // que já destaca o próprio botão — ver o motivo documentado no
        // tour de honorários). calcular() é assíncrono (chama a API), mas
        // como #cpResultado começa com display:none e só fica visível
        // depois do cálculo, o próprio waitForElement() do motor já
        // espera o resultado ficar pronto antes de medir/destacar.
        onBeforeShow: () => { document.getElementById('cpBtnCalcular')?.click(); },
      },
      {
        id: 'prazos-feriados',
        page: '/pages/calculadora-prazos.html',
        selector: '#cpFeriadosWrapper',
        title: 'Feriados no intervalo',
        text: 'Os feriados nacionais, estaduais e municipais dentro do período aparecem aqui — eles já entram automaticamente na contagem quando o tipo é "Dias úteis".',
        placement: 'left',
        planRequired: 'Plus',
      },
      {
        id: 'prazos-criar',
        page: '/pages/calculadora-prazos.html',
        selector: '#cpBtnCriarPrazo',
        title: 'Crie a tarefa de prazo',
        text: 'No canto superior direito do resultado, use "+ Criar prazo" para gerar uma tarefa já com a data e os dias calculados aqui.',
        placement: 'bottom',
        planRequired: 'Plus',
      },
      {
        id: 'prazos-criar-modal',
        page: '/pages/calculadora-prazos.html',
        selector: '#modalCriarPrazo .modal',
        title: 'Tarefa pré-preenchida',
        text: 'O modal já vem com a data de vencimento, os dias e o tipo de contagem preenchidos — só falta descrever a tarefa e, se quiser, vincular a um processo. Não vamos cadastrar agora, é só para você conhecer a tela.',
        placement: 'auto',
        planRequired: 'Plus',
        // Só clica no botão agora (não no passo anterior, que já o
        // destaca — mesmo motivo documentado no tour de honorários).
        // openModal() é assíncrono (carrega a lista de processos), mas
        // como o modal só ganha a classe "open" depois disso, o próprio
        // waitForElement() do motor já espera isso terminar antes de
        // medir/destacar.
        onBeforeShow: () => { document.getElementById('cpBtnCriarPrazo')?.click(); },
      },
    ],
    // O último passo abre o modal "+ Criar prazo" só para mostrar a tela
    // (nada é cadastrado). Ao concluir o tour, fecha o modal de novo.
    onComplete: () => {
      document.getElementById('modalCriarPrazo')?.classList.remove('open');
    },
  },
  {
    id: 'tema',
    label: '🎨 Tema',
    steps: [
      {
        id: 'tema-presets',
        page: '/pages/tema.html',
        selector: '[data-tour="tema-presets"]',
        title: 'Temas prontos',
        text: 'Escolha entre vários temas prontos, claros e escuros — clique em qualquer um para aplicar e ver o resultado na hora, em toda a tela.',
        placement: 'right',
        planRequired: 'Plus',
      },
      {
        id: 'tema-moderno',
        page: '/pages/tema.html',
        // O menu lateral inteiro (.sidebar) não é um bom alvo pra
        // destacar: é uma coluna com a altura de toda a página, então o
        // realce ficava maior que a tela (às vezes ainda pior depois de
        // trocar de tema, se a troca expandir grupos do menu). O card do
        // preset já é pequeno e estável, então destaca ele — o efeito da
        // cor aplicada em toda a tela (menu lateral incluído) já fica
        // visível por conta própria, sem precisar de um realce específico.
        selector: '.preset-card[data-preset-id="moderno"]',
        title: 'Tema "Moderno" aplicado',
        text: 'Assim que você clica, o tema já é aplicado em toda a tela — repare no menu lateral. Nada é salvo ainda.',
        placement: 'top',
        planRequired: 'Plus',
        onBeforeShow: () => { document.querySelector('.preset-card[data-preset-id="moderno"]')?.click(); },
      },
      {
        id: 'tema-dracula',
        page: '/pages/tema.html',
        selector: '.preset-card[data-preset-id="dracula"]',
        title: 'Tema "Dracula" aplicado',
        text: 'Experimente quantos temas quiser — cada clique já mostra o resultado em toda a tela, só confirma quando clicar em Salvar.',
        placement: 'top',
        planRequired: 'Plus',
        onBeforeShow: () => { document.querySelector('.preset-card[data-preset-id="dracula"]')?.click(); },
      },
      {
        id: 'tema-logo',
        page: '/pages/tema.html',
        selector: '[data-tour="tema-logo"]',
        title: 'Personalize com sua marca',
        text: 'Além dos temas prontos, você pode ajustar cada cor individualmente na seção "Personalizar" e adicionar a logo do seu escritório aqui — ela aparece no cabeçalho do sistema e do portal do cliente.',
        placement: 'top',
        planRequired: 'Plus',
      },
      {
        id: 'tema-salvar',
        page: '/pages/tema.html',
        selector: '#btnSalvar',
        title: 'Salve para valer',
        text: 'No fim da lista, clique em "Salvar tema" para aplicar de verdade — até lá, tudo o que você viu é só pré-visualização.',
        placement: 'top',
        planRequired: 'Plus',
      },
    ],
  },
];
