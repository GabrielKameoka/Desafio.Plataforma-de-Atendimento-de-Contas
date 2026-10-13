# Desafio: Plataforma de Atendimento de Contas

## Execução local — T-01

Pré-requisito: Docker com Compose. Na raiz do repositório, execute:

```bash
docker compose up --build
```

Abra <http://localhost:8080>. A página inicial lista dois atendentes, uma supervisora e dois clientes fictícios. O host aplica a migração inicial do EF Core e insere esses registros com identificadores fixos; reiniciar o Compose não os duplica. Para conferir os serviços, acesse <http://localhost:8080/health> e execute `docker compose ps`. O PostgreSQL fica restrito à rede do Compose; o simulador de CRM responde em `/health` nessa rede.

Para compilar e executar os testes da solução localmente, use `dotnet build Desafio.Plataforma-de-Atendimento-de-Contas.sln` e `dotnet test Desafio.Plataforma-de-Atendimento-de-Contas.sln`. A T-01 entrega a base de execução. Identidade, casos, histórico e sincronização serão implementados nas tarefas seguintes; a presença de usuários na página ainda não cria sessão.

## Contexto

Uma instituição financeira atende clientes por diferentes canais. Durante um contato, o atendente precisa entender rapidamente o contexto da conta, consultar solicitações anteriores e registrar o que foi combinado. Quando a informação fica espalhada entre sistemas, o atendimento perde continuidade, os prazos ficam difíceis de acompanhar e integrações com ferramentas de relacionamento podem falhar sem que a equipe perceba.

## Problema

Como oferecer aos atendentes uma visão confiável do histórico do cliente e um fluxo claro para tratar solicitações, sem perder informações quando dois profissionais atuam no mesmo caso ou quando um sistema externo está indisponível?

## Objetivo

Construa uma **plataforma fictícia de atendimento de contas**. Ela deve permitir consultar o contexto de um cliente, abrir e acompanhar solicitações, registrar interações, controlar prazos e sincronizar informações com um CRM simulado.

Use **C# e .NET em toda a aplicação, inclusive na interface**. Trabalhe somente com dados fictícios. O projeto é uma simulação de engenharia: não deve usar marcas, identidades visuais, dados reais ou alegar que reproduz sistemas internos de qualquer instituição.

## Cenário principal

Uma cliente questiona uma tarifa lançada em sua conta. O atendente localiza seu cadastro fictício, visualiza as interações registradas em solicitações anteriores, abre uma contestação e recebe um protocolo. Outro atendente assume a solicitação, registra uma interação e atualiza o andamento. Quando a análise termina, a solicitação é resolvida com uma justificativa. Um supervisor consegue identificar casos próximos do prazo e verificar o histórico das decisões.

Esse cenário deve funcionar integralmente pela interface. A integração com o CRM deve ocorrer sem impedir o registro do atendimento quando o serviço externo falhar.

## Perfis e funcionalidades

### Atendente

- Consultar clientes fictícios identificados apenas por ID e nome fictícios.
- Ver solicitações abertas e o histórico de interações registradas nas solicitações de um cliente.
- Abrir uma contestação de tarifa e receber um protocolo único.
- Assumir ou atribuir qualquer solicitação, registrar interações e alterar seu status.
- Resolver uma solicitação com justificativa e consultar seu histórico de alterações.

### Supervisor

- Consultar solicitações por status, prioridade, responsável e prazo.
- Identificar solicitações próximas do vencimento ou vencidas.
- Consultar o histórico de uma solicitação e reabri-la com motivo registrado.

## Regras obrigatórias

1. Cada solicitação possui protocolo único, cliente, tipo, status, prioridade, responsável e prazo de atendimento. Na abertura, o sistema define prioridade Média, atribui o caso ao atendente que o abriu e calcula o prazo em dias úteis conforme a prioridade.
2. Os status são **Aberta**, **Em análise**, **Aguardando cliente** e **Resolvida**. Documente as transições permitidas. A reabertura de uma solicitação resolvida exige motivo e registro no histórico.
3. Toda mudança de status, prioridade, responsável ou prazo registra quem fez a alteração, quando ela ocorreu e o motivo informado.
4. Uma alteração baseada em dados desatualizados não pode sobrescrever silenciosamente uma alteração mais recente feita por outro atendente. A interface deve informar o conflito.
5. A interface gera uma chave para cada tentativa de abertura e a preserva se precisar repetir a tentativa. Repetir a mesma requisição não pode gerar dois protocolos; reutilizar a mesma chave com dados diferentes deve ser tratado como conflito.
6. Uma falha na sincronização com o CRM não pode apagar nem desfazer uma solicitação já registrada. O sistema deve mostrar que a sincronização está pendente ou falhou e permitir sua recuperação.
7. O cadastro usa apenas ID e nome fictícios, sem documento ou número de conta. Nome, descrições e interações não devem aparecer em logs, métricas ou mensagens de erro.
8. Qualquer atendente pode consultar e alterar qualquer solicitação. A reabertura de um caso resolvido é exclusiva do supervisor. O projeto pode usar usuários de demonstração; não deve depender de credenciais reais.

## Stack e limites

| Área | Tecnologia ou restrição |
| --- | --- |
| Interface | Blazor |
| Backend | ASP.NET Core e C# |
| Persistência local | Entity Framework Core com DB de sua preferência |
| Testes | xUnit e testes de integração dos fluxos críticos |
| Observabilidade | Logs estruturados, identificador de correlação e métricas básicas com OpenTelemetry |
| Integração | Adaptador para um CRM simulado, sem dependência de uma conta em serviço comercial |
| Execução | Um comando documentado para iniciar a demonstração com dados fictícios |

Organize a aplicação como um **monólito modular**, com limites claros entre atendimento, consulta de clientes e integração. Microsserviços, infraestrutura de nuvem e operações financeiras reais não fazem parte do desafio.

## Entregas

### Essencial

- Interface funcional para percorrer o cenário principal.
- API, persistência, migrações e dados fictícios de demonstração.
- Regras de status, auditoria, prevenção de duplicidade e tratamento de concorrência.
- CRM simulado com possibilidade de provocar uma falha e observar a recuperação.
- Testes automatizados para as regras e os fluxos de maior risco.

### Documentação

- `README.md` com objetivo, requisitos para execução, comando de inicialização e roteiro de demonstração.
- Diagrama simples dos módulos e do fluxo de sincronização com o CRM.
- Registro de pelo menos **três decisões técnicas**, explicando a escolha, a alternativa considerada e o custo da decisão.
- Explicação dos limites da simulação e das melhorias que seriam necessárias para operar com dados reais.

## Critérios de aceite

Considere o desafio concluído quando outra pessoa conseguir, seguindo apenas o README:

1. Iniciar a aplicação e entrar com um usuário fictício.
2. Encontrar uma cliente, abrir uma contestação e consultar o protocolo e o histórico.
3. Alterar responsável e status, registrar uma interação e resolver o caso com justificativa.
4. Identificar no painel do supervisor uma solicitação próxima do prazo.
5. Repetir uma abertura sem criar protocolo duplicado.
6. Simular uma edição concorrente e receber uma mensagem clara de conflito.
7. Indisponibilizar o CRM simulado, confirmar que o atendimento continua salvo e observar a sincronização após a recuperação.
8. Executar os testes automatizados e verificar que os dados exibidos e registrados são fictícios.

## Como apresentar o projeto

Prepare uma demonstração de **3 a 5 minutos** centrada na jornada de atendimento. Mostre o caso na interface, o histórico de decisões, uma falha controlada do CRM e a recuperação da integração. Depois explique as decisões que preservam os dados e permitem investigar problemas. Deixe o código e o diagrama disponíveis para quem quiser aprofundar a avaliação técnica.
