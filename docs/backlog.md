# Backlog de execução

Responsável: aluno, com apoio do Codex. Sem prazo de calendário. Atualizado em 14 de outubro de 2026.

**Estado atual:** M0–M8 concluídos em 14 de outubro de 2026. B80 reuniu os dez cenários em Android/Windows com API/MySQL isolados; evidências e limites. B81 revisou a interação nos dois alvos; evidências e limites. B82 preparou e ensaiou o ambiente fictício; evidências e limites. B83 consolidou a entrega; evidências finais.

Estados permitidos: Planejado → Em andamento → Em verificação → Concluído. Bloqueado deve registrar causa e condição de saída. Manter uma tarefa ativa por vez no desenvolvimento individual.

## M0 — Planejamento e contexto

| ID | Tarefa | Estado | Evidência |
|---|---|---|---|
| B00 | Organizar instruções e documentos para Codex | Concluído | AGENTS.md, regras e workflow com links locais verificados |
| B01 | Registrar escopo e escolhas do aluno | Concluído | Requisitos e decisões distinguem exigências, escolhas e propostas |
| B02 | Ordenar entregas e definir critérios | Concluído | 34 tarefas com dependências válidas/sem ciclos e 11 requisitos vinculados a tarefas |

Fechamento em 7 de outubro de 2026: registro de execução do M0, com critérios, mapa de requisitos e resultado da auditoria. O M1 foi iniciado por solicitação do aluno.

## M1 — Especificação para implementar

Depende de M0. Saída: primeira fatia funcional pode ser implementada sem inventar contratos durante a construção da tela.

| ID | Tarefa | Depende de | Estado | Critério e evidência |
|---|---|---|---|---|
| B10 | Fechar modelo relacional | B01 | Concluído | Modelo físico: tipos, nulabilidade, tamanhos, FKs, índices e snapshots em 10 tabelas MySQL e 7 SQLite |
| B11 | Especificar contratos e exemplos HTTP | B10 | Concluído | [OpenAPI](api/openapi.json): 19 operações, 34 schemas atuais (33 no fechamento do M1), erros e replay; [37 casos de exemplo atuais (35 no fechamento do M1)](api/examples.json) verificados |
| B12 | Especificar navegação e formulários | B01 | Concluído | 11 telas: Cliente/Empresa, campos, estados e diferenças Android/Windows |
| B13 | Fechar regras de data, limites e atendimento | B10, B11, B12 | Concluído | RB01–RB09 e sincronização: limites, data, troco, cancelamento, versões e transições |

Fechamento em 8 de outubro de 2026: registro do M1. `node scripts/validate-planning.mjs` passou na raiz, no Windows com Node.js, sem pacotes externos. Verificados documentos, referências e exemplos; o script não substitui um validador OpenAPI completo. SQL, API e telas ainda não executados. Compatibilidade de versões e comunicação real ficam para M2.

## M2 — Prova técnica e fundação

Depende de M1. Saída: executar o menor percurso real no Android e no Windows.

| ID | Tarefa | Depende de | Critério de aceite |
|---|---|---|---|
| B20 | Inspecionar ambiente | B13 | Concluído — diagnóstico do M2: SDK, workloads, JDK, Android SDK, Windows SDK e MySQL verificados; a ausência inicial de emulador foi resolvida em B24 |
| B21 | Criar solução e dependências | B20 | Concluído — [solução](../VanderBeer.slnx), referências conforme arquitetura, restore e builds API/MAUI Android/Windows sem avisos ou erros; versões e comandos |
| B22 | Criar banco e primeira evolução SQL | B21 | Concluído — [migration e seed](../database/), executor com checksum; 10 tabelas, 8 FKs e 48 CHECKs aplicados em MySQL 8.0.46 temporário; reaplicação ignorada e seed repetido sem duplicar (evidência) |
| B23 | Conectar API ao MySQL | B22 | Concluído — `/health` e `/api/v1/categories` retornaram 200 do MySQL temporário; porta do banco indisponível retornou 503 Problem em ambos (evidência) |
| B24 | Executar MAUI nos dois alvos | B21, B23 | Concluído — Windows e emulador Android 15 exibiram categorias da API; contador SQLite passou de 1 para 2 após reiniciar cada app (evidência) |
| B25 | Documentar execução e primeira CI | B23, B24 | Concluído — README com comandos de migração, API, Windows e Android; [CI inicial](../.github/workflows/ci.yml) para builds API/migrador, 2 testes HTTP e validador documental; comandos passaram localmente, execução remota da CI ainda não comprovada (evidência) |

M2 concluído em 8 de outubro de 2026. A prova usou MySQL 8.0.46 temporário e emulador Android 15; o serviço MySQL principal, MySQL 8.4, dispositivo Android físico e execução remota da CI ainda não foram verificados. Na conclusão do M2, o próximo item era B30.

## M3 — Catálogo e gestão de produtos

Depende de M2. Requisitos RF01 e RF10.

| ID | Tarefa | Depende de | Critério de aceite |
|---|---|---|---|
| B30 | Produtos, categorias e formas de pagamento na API | B25 | Concluído — seed no MySQL 8.0.46 temporário; filtros, paginação, validação, versões e replay verificados (evidência) |
| B31 | Área Empresa: lista e formulário de produto | B30 | Concluído — criar, editar e inativar em Windows/Android; erros por campo, confirmação da API e recuperação manual após resposta perdida/reinício (evidência) |
| B32 | Área Cliente: catálogo e detalhe | B30 | Concluído — pesquisa, categoria, preço, imagem/alternativa e disponibilidade da API nos dois alvos (evidência) |
| B33 | Cache e estados de tela | B31, B32 | Concluído — produto da Empresa apareceu no Cliente após atualização; cache parcial com horário/aviso sem conexão nos dois alvos (evidência) |

M3 concluído em 8 de outubro de 2026; registro de execução. A prova usou instância MySQL temporária e emulador Android; serviço principal, MySQL 8.4 e aparelho físico ainda não foram verificados. Na conclusão do M3, o próximo item era B40.

## M4 — Cliente e carrinho

Depende de M3. Requisitos RF02 e RF03.

| ID | Tarefa | Depende de | Critério de aceite |
|---|---|---|---|
| B40 | Cadastro da API e identificação local | B33 | Concluído — GET/POST/PUT PF/PJ, migrations 002/003, validação e replay exercitados no MySQL temporário; persistência/reabertura do mesmo `CustomerStore` comprovada com SQLite real; builds Windows/Android passaram. Evidência. CPF/CNPJ duplicado não transfere identidade |
| B41 | Tela de perfil e endereço | B40 | Concluído — obrigatórios/opcionais e estado de envio implementados; reabertura da pendência comprovada em Windows, Android e teste SQLite. Evidência |
| B42 | Carrinho persistente | B33 | Concluído — adicionar/remover/alterar, reinício e total estimado comprovados no Windows, Android e SQLite. Evidência |

M4 está concluído. A troca para Empresa não apaga o cadastro Cliente nem o carrinho. O perfil PF/PJ e o carrinho usam o mesmo arquivo SQLite do catálogo sem descartar as tabelas anteriores. A migração local versionada foi incorporada em M5/M6; consulte M6 e modelo relacional.
A migração do esquema preservou as tabelas de catálogo e comandos administrativos pendentes; consulte modelo relacional para o estado comprovado.

## M5 — Pedido online de ponta a ponta

Depende de M4. Requisitos RF04, RF05, RF06 e RF07.

| ID | Tarefa | Depende de | Critério de aceite |
|---|---|---|---|
| B50 | Criar pedido na API | B41, B42 | Preço oficial, validação, transação, snapshots, protocolo e idempotência |
| B51 | Finalização no app | B50 | Concluído — modalidade/data/endereço/pagamento/resumo, cache de referências e transação SQLite verificados; evidência |
| B52 | Envio e histórico | B51 | Concluído — corpo/chave da outbox congelados antes do HTTP; recibo e estado local gravados atomicamente; envio, reenvio e histórico sem duplicação verificados no Windows e emulador Android (evidência) |

B50–B52 concluídas em 12/10/2026; evidências em B50, B51 e B52. Na conclusão do M5, processamento automático, dependências de cadastro e política completa de repetição estavam previstos para M6.

## M6 — Sincronização e recuperação

Depende de M5. Requisitos RF05 e RF06.

| ID | Tarefa | Depende de | Critério de aceite |
|---|---|---|---|
| B60 | Fila e dependências de cadastro | B52 | Cadastro precede pedidos; chave/corpo persistentes; recuperação após interrupção |
| B61 | Falhas transitórias e revisão | B60 | Espera crescente, limite por ciclo, ação Reenviar e erros definitivos diferenciados |
| B62 | Testar duplicação e concorrência | B61 | Mesmo envio simultâneo, timeout após commit e replay após preço alterado geram um pedido |
| B63 | Validar offline nos dois alvos | B62 | Sem rede → salvar → fechar → abrir → reconectar → obter um protocolo |

Implementação e verificação em 13/10/2026 (registro M6):

| Tarefa | Estado | Evidência |
|---|---|---|
| B60 — Fila e dependências | Concluído | Transação SQLite, chave/corpo/endereço persistentes e reabertura cobertos por testes. Atualização pendente → pedido dependente → confirmação ordenada comprovada no Android. Criação pendente → pedido dependente → reinício offline → confirmação ordenada comprovada pela interface Windows, SQLite e API/MySQL, com um pedido por chave (M6). |
| B61 — Falhas e revisão | Concluído | Esperas, limite de ciclo, reenvio e diferenciação de falhas implementados; tentativas automáticas vistas nos dois alvos. Rejeição por mudança de preço exibida e revisão com nova chave na mesma entrada comprovada na interface Android; revisão também comprovada no Windows. |
| B62 — Duplicação | Concluído | Preparação simultânea testada localmente; concorrência HTTP, timeout após commit e replay após preço alterado comprovados em B50 e B52. |
| B63 — Offline | Concluído | API desligada → pedido salvo → reinício → API religada → um protocolo e um recibo, comprovados no Windows e emulador Android. APK final instalado preservando o SQLite e protocolo conferido após nova abertura. |

Não prometer sincronização contínua com aplicativo fechado.

## M7 — Atendimento e recompra

Depende de M6. Requisitos RF08, RF09 e RF11.

| ID | Tarefa | Depende de | Critério de aceite |
|---|---|---|---|
| B70 | Área Empresa: pedidos e atendimento | B63 | Filtrar fila; ver detalhes; transições e versão verificadas no servidor; recuperar resposta perdida com a mesma chave |
| B71 | Atualização do histórico do cliente | B70 | Refresh mostra atendimento; estado de envio continua separado |
| B72 | Pedir novamente | B71 | Novo carrinho com preços atuais; indisponibilidade exige revisão; pedido anterior intacto |

**B70 concluído:** E03/E04 exibem fila, filtros, detalhe, ações válidas e recuperação manual; o comando é salvo no SQLite antes do PATCH. API/MySQL 8.0.46 temporário comprovaram transições, versão, concorrência e replay após perda de resposta; a recuperação após reinício passou na interface Windows, e uma transição normal passou no emulador Android. A linha da fila agora muda junto do detalhe. Builds Windows/Android, 42 testes unitários, 8 HTTP e validação documental passaram. Registro e limites.

**B71 concluído:** o refresh consulta páginas/detalhes pela API, une por ID/chave e atualiza o detalhe aberto. Lista e detalhe separam envio e atendimento. A transição `Received → Confirmed` feita pela área Empresa no Windows apareceu após refresh no histórico Cliente do Windows e do emulador Android, inclusive no detalhe já aberto; o MySQL temporário registrou versão 2 e dois eventos. Teste SQLite anterior comprova que resposta antiga não regride estado mais novo. Registro e limites.

**B72 concluído por orientação do aluno:** a ação Pedir novamente consulta o pedido e cada produto atual pela API; a tela de revisão mostra preço anterior/atual e obriga remover explicitamente itens indisponíveis. Se há carrinho, pede confirmação antes de substituí-lo. A confirmação consulta novamente os produtos e, se houve mudança, exige nova revisão; o novo carrinho é gravado em transação SQLite, sem alterar pedido ou chave anteriores. Em 13/10/2026, passaram 41 testes unitários, incluindo substituição, rollback e preservação do pedido original, builds MAUI Windows/Android e prova funcional nas duas interfaces com API HTTP local fictícia. O pedido original permaneceu no histórico com seus valores; o SQLite anterior foi preservado. A prova B72 não usou MySQL nem a API ASP.NET real; o ensaio integrado posterior com ambos está em B80. Detalhes em B72.

Alteração administrativa exige conexão. Comandos iniciados online devem ser persistidos para recuperação manual, conforme sincronização; isso não permite novas edições offline nem envio automático de comandos administrativos.

## M8 — Acabamento e entrega

Depende de M7. Requisitos RF01–RF11.

| ID | Tarefa | Depende de | Critério de aceite |
|---|---|---|---|
| B80 | Verificar matriz funcional | B72 | Concluído — dez cenários reunidos em um ensaio com API/MySQL e interfaces Android/Windows; correção de moeda, resultados e limites em B80. |
| B81 | Revisar interface nos dois alvos | B80 | Concluído — toque, Voltar e teclado no emulador Android; Tab, foco e redimensionamento no Windows; data, moeda e nomes acessíveis ajustados. Builds dos dois alvos e 56 testes unitários passaram (evidência). |
| B82 | Preparar ambiente de demonstração | B81 | Concluído — README atualizado, prévia sem gravações, seed fictício idempotente, catálogo Android/Windows e roteiro HTTP ensaiados; evidência |
| B83 | Registrar evidências finais | B82 | Concluído — versões, comandos/resultados, limites reais e material de apresentação registrados em B83 |

B80–B83 estão concluídos. O M8 está fechado com evidências e limitações reais em B83. A apresentação pode seguir o roteiro.

## Como atualizar uma tarefa

Registrar estado, alteração entregue, arquivos principais, comando/cenário de validação, resultado e pendências. Caso o requisito mude, manter o ID e ajustar dependências e aceite. Novo trabalho fora do recorte entra como extensão, sem substituir silenciosamente uma obrigação existente.

## Manutenção — reorganização da experiência (B90)

Solicitada pelo aluno após orientação do professor: priorizar cadastro e compra do cliente, separar visualmente a gestão e reduzir formulários extensos. M0–M8 permanecem encerrados; esta é uma manutenção posterior.

| ID | Tarefa | Estado | Critério e evidência |
|---|---|---|---|
| B90 | Reorganizar navegação, cadastro, compra e gestão | Em verificação | Cadastro em duas etapas; quatro destinos do cliente; gestão isolada; checkout em três etapas; dados anteriores preservados. Registro em B90. |

## Publicação do repositório

Por escolha do autor, apenas este backlog é publicado entre os documentos Markdown. Os registros detalhados citados no histórico são mantidos localmente. O estado B90 permanece em verificação.

O README também integra a publicação, com apresentação, funcionalidades e instruções de execução; os demais Markdown continuam locais.
