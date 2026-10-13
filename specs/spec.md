# Especificação funcional: Plataforma de Atendimento de Contas

**Fonte:** `README.md` da raiz do projeto  
**Estado:** decisões funcionais confirmadas para a primeira versão  
**Escopo:** demonstração com dados fictícios

## 1. Problema e objetivo

Durante um atendimento, as informações do cliente, suas solicitações e o histórico de contatos precisam estar disponíveis em um único fluxo. A plataforma deve permitir que atendentes registrem e acompanhem uma contestação de tarifa, que supervisores acompanhem prazos e que uma falha do CRM não provoque perda do atendimento.

O resultado esperado é uma jornada demonstrável pela interface, com histórico confiável, proteção contra duplicidade e conflitos de edição visíveis. A plataforma representa uma instituição fictícia e não executa operações financeiras.

## 2. Usuários

- **Atendente:** consulta clientes e todas as solicitações, abre casos, registra interações, assume ou transfere qualquer caso e atualiza seu andamento.
- **Supervisor:** consulta a carteira de casos e seus prazos, verifica o histórico e reabre casos resolvidos mediante justificativa.
- **Pessoa avaliadora:** inicia a aplicação com dados e usuários fictícios e percorre o cenário de demonstração descrito no README.

## 3. Jornadas e critérios de aceite

### US-01 — Consultar o contexto de um cliente (prioridade P1)

Como atendente, quero localizar um cliente e ver suas solicitações e contatos anteriores para continuar o atendimento com contexto.

**Aceite**

1. Dado um cliente fictício existente, quando o atendente o localizar, então verá seu identificador e nome fictícios, solicitações e histórico de contatos.
2. O histórico de contatos do cliente reunirá as interações registradas em suas solicitações; contatos sem solicitação não fazem parte desta versão.
3. A consulta não exibirá informações de outro cliente como se pertencessem ao cliente selecionado.

### US-02 — Abrir contestação de tarifa (prioridade P1)

Como atendente, quero registrar uma contestação e receber um protocolo para que o caso possa ser acompanhado.

**Aceite**

1. Dado um cliente existente e uma descrição não vazia, quando um atendente abrir uma contestação, então haverá uma solicitação com protocolo único, tipo **Contestação de tarifa**, prioridade **Média**, status **Aberta**, responsável igual ao atendente que a abriu, data de abertura e prazo.
2. A prioridade e o responsável iniciais serão definidos pelo sistema; a pessoa que abre o caso não os informa. Eles poderão ser alterados posteriormente, com motivo e registro no histórico.
3. Dados inválidos ou cliente inexistente não criarão uma solicitação parcial.
4. A interface gerará uma chave única para cada tentativa de abertura e conservará a mesma chave ao repetir a tentativa após falha ou ausência de resposta. A mesma chave e o mesmo conteúdo devolverão o protocolo original sem criar outro caso.
5. Dada a mesma chave com conteúdo diferente, o sistema informará o conflito e não alterará o caso original.

### US-03 — Conduzir e resolver um caso (prioridade P1)

Como atendente, quero assumir ou transferir um caso, registrar interações e mudar seu andamento até a resolução para manter o atendimento rastreável.

**Aceite**

1. Atribuir ou trocar o responsável, mudar status, prioridade ou prazo exigirá autor e motivo e acrescentará um evento ao histórico.
2. Uma interação registrada aparecerá na linha do tempo do caso com autor e momento do registro.
3. A resolução exigirá justificativa. Depois de resolvido, o caso não aceitará mudanças comuns de status, responsável, prioridade, prazo ou interações até ser reaberto.
4. Uma transição de status não permitida será rejeitada sem modificar o caso ou seu histórico.
5. Se duas pessoas editarem a mesma versão do caso, a segunda gravação não substituirá silenciosamente a primeira; a interface explicará o conflito e oferecerá a leitura do estado atual.
6. Um atendente poderá consultar e alterar qualquer solicitação, mesmo quando outro atendente for o responsável atual. A reabertura continuará reservada ao supervisor.

### US-04 — Supervisionar prazos e reabrir casos (prioridade P1)

Como supervisor, quero filtrar solicitações e reabrir um caso resolvido com motivo para acompanhar a qualidade e os prazos do atendimento.

**Aceite**

1. O supervisor poderá filtrar por status, prioridade, responsável e prazo, identificando casos próximos do vencimento e vencidos.
2. A reabertura de um caso resolvido exigirá motivo e registrará autor e momento no histórico.
3. Um atendente sem permissão de supervisão não poderá executar a reabertura.

### US-05 — Sincronizar com CRM simulado (prioridade P1)

Como equipe de atendimento, quero que as alterações relevantes sejam enviadas ao CRM para manter o contexto disponível em outro sistema sem perder o registro local em caso de falha.

**Aceite**

1. Após a abertura ou atualização relevante, a plataforma registrará que há informação a sincronizar.
2. Se o CRM estiver indisponível, a solicitação continuará consultável e íntegra, e o estado da sincronização indicará pendência ou falha.
3. Quando o CRM voltar a responder, o envio poderá ser retomado; o resultado ficará visível para diagnóstico.
4. Repetir um envio não poderá criar registros inconsistentes no CRM simulado.

### US-06 — Investigar o atendimento (prioridade P2)

Como supervisor ou pessoa avaliadora, quero acompanhar eventos e sinais operacionais para entender o que aconteceu em um caso e em uma falha de integração.

**Aceite**

1. Cada atendimento terá histórico cronológico consultável, sem apagar eventos anteriores.
2. Uma falha poderá ser relacionada à operação que a originou por um identificador de correlação.
3. Logs, métricas e mensagens de erro não conterão nome de cliente nem conteúdo de interações ou descrições de solicitações.
4. A demonstração apresentará medidas básicas de volume de casos, falhas de sincronização e duração das operações principais.

## 4. Regras de negócio

### Ciclo de vida da solicitação

| Estado atual | Ação permitida | Próximo estado |
| --- | --- | --- |
| Aberta | Iniciar análise | Em análise |
| Em análise | Solicitar informação ao cliente | Aguardando cliente |
| Aguardando cliente | Retomar análise | Em análise |
| Em análise | Resolver com justificativa | Resolvida |
| Resolvida | Reabrir com motivo, por supervisor | Em análise |

Outras transições são inválidas nesta versão. A mudança de responsável ou prioridade não muda o status automaticamente. O prazo pode ser alterado por uma ação explícita e justificada; seu novo valor não pode anteceder a abertura da solicitação.

### Prazo inicial e acompanhamento

- O prazo inicial será calculado pela prioridade: **Alta = 1 dia útil**, **Média = 3 dias úteis** e **Baixa = 5 dias úteis**. A contestação começa com prioridade Média e prazo de 3 dias úteis.
- Para esta demonstração, dias úteis são de segunda a sexta-feira; feriados não entram no cálculo. A contagem começa no primeiro dia útil após a abertura, e o vencimento ocorre ao fim do último dia útil. São regras fictícias, sem compromisso com prazos praticados por instituições reais.
- Uma solicitação é considerada próxima do vencimento nas 24 horas corridas anteriores ao prazo. Atrasos continuam visíveis enquanto o caso não estiver resolvido, inclusive quando estiver **Aguardando cliente**.
- Mudar a prioridade não recalcula o prazo automaticamente. Alterar o prazo exige uma ação separada, motivo e evento de auditoria.

### Auditoria e integridade

- Cada solicitação tem exatamente um cliente, um protocolo único e um responsável atual. Na abertura, o responsável é o atendente que realizou a ação.
- Mudanças de status, prioridade, responsável e prazo geram eventos de auditoria com valor anterior, valor novo, autor, data e motivo. A reabertura também gera um evento próprio.
- O histórico é acrescentado ao longo do tempo; eventos anteriores não podem ser editados ou removidos pelas ações usuais da aplicação.
- A mesma abertura, identificada pela chave gerada na interface e preservada em novas tentativas, produz no máximo uma solicitação. A chave fica vinculada ao conteúdo da abertura e permanece válida durante a demonstração.
- Alterações concorrentes devem ser detectadas antes de substituir o estado persistido.
- A conclusão de uma operação local não depende da disponibilidade do CRM.

### Permissões e privacidade

- Atendentes e supervisores usam identidades fictícias com papéis distintos.
- Qualquer atendente autenticado pode consultar e alterar qualquer solicitação. Apenas o supervisor pode reabrir uma solicitação resolvida.
- O cadastro de cliente contém somente identificador e nome fictícios. Não serão coletados documento, número de conta ou outros campos que precisem de máscara nesta versão. Nome de cliente, descrição e interações não serão incluídos em registros operacionais.

## 5. Informações do domínio

- **Cliente:** identificador e nome fictícios necessários para localização e visualização do contexto.
- **Solicitação:** protocolo, cliente, tipo, título ou descrição, prioridade, status, responsável atual, abertura, prazo e versão de edição.
- **Interação:** mensagem ou resumo de contato associado a uma solicitação, com autor e momento. As interações das solicitações de um cliente formam seu histórico de contatos.
- **Evento de auditoria:** mudança ocorrida na solicitação, com valores anterior e novo, autor, motivo e momento.
- **Sincronização com CRM:** identificação da alteração a enviar, estado do envio e informações de tentativa suficientes para diagnóstico e recuperação.

Esses conceitos descrevem o comportamento esperado. A distribuição em classes, tabelas e serviços será definida no plano técnico.

## 6. Casos de borda

- Cliente não encontrado ou solicitação inexistente.
- Abertura sem cliente, sem descrição válida ou sem chave de requisição; abertura em sexta-feira ou fim de semana, para verificar o cálculo em dias úteis.
- Tentativa de resolver sem justificativa, reabrir sem motivo ou executar uma transição proibida.
- Atribuição para atendente inexistente ou indisponível.
- Repetição de abertura após perda da resposta original.
- Edição sobre versão antiga do caso.
- CRM indisponível, resposta de erro ou recuperação após falha.
- Solicitação cujo prazo venceu enquanto aguarda resposta do cliente: ela continua visível na supervisão; este desafio não pausa automaticamente o prazo.

## 7. Requisitos de qualidade e restrições

- Toda a aplicação será desenvolvida em C# e .NET, incluindo a interface em Blazor.
- A demonstração funcionará com dados fictícios e sem contas de serviços comerciais.
- O fluxo completo deverá ser executável pela interface e inicializável por um comando documentado.
- Os fluxos críticos terão testes automatizados, especialmente transições de estado, auditoria, idempotência, concorrência e falha de integração.
- A solução será um monólito modular. Escolhas de API, persistência, padrões, bibliotecas e organização do código pertencem à constituição e ao plano técnico.

## 8. Critérios de sucesso

1. Uma pessoa que não desenvolveu o projeto consegue iniciar a demonstração seguindo o README.
2. Essa pessoa consegue concluir pela interface a jornada **localizar cliente → abrir contestação → atribuir → registrar interação → resolver**.
3. Os cenários de duplicidade, conflito de edição e indisponibilidade do CRM podem ser reproduzidos sem modificar dados manualmente.
4. O histórico permite reconstruir quem mudou o caso, quando e por qual motivo.
5. Os testes automatizados dos fluxos críticos são executados pelo comando documentado.

## 9. Decisões confirmadas e detalhe pendente

- A sincronização e a demonstração de falha do CRM são obrigatórias para concluir o desafio; por isso, US-05 é P1.
- Atendentes podem consultar e alterar qualquer caso. Supervisores podem consultar todos os casos e são os únicos que podem reabri-los.
- O prazo inicial segue os dias úteis por prioridade definidos acima, sem calendário de feriados nesta versão.
- O cadastro contém apenas identificador e nome fictícios; não há campos que exijam máscara.
- A abertura exige cliente e descrição. O sistema define prioridade Média e responsável igual ao atendente que abriu o caso.
- A interface gera uma chave por tentativa de abertura e a preserva nas novas tentativas dessa mesma abertura.
- O histórico de contatos do cliente reúne somente interações ligadas às suas solicitações.
- **Detalhe a definir no plano técnico:** quais dados são enviados ao CRM simulado e como as tentativas de envio serão retomadas. Abertura, mudança de status e troca de responsável são os eventos candidatos; o contrato deverá preservar os dados locais e evitar duplicações no destino.
