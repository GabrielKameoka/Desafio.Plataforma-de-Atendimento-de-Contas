# Constituição do projeto

## Stack
- .NET 10, Minimal API no backend e blazor no frontend
- Postgresql via EF Core
- Testes com xUnit

## Arquitetura
- Endpoint não acessa db diretamente, sempre via repositório com Repository Pattern usando interfaces
- Regra de negócio sempre nos seus respectivos modelos, nunca no endpoint

## Qualidade
- Endpoint sempre retorna um modelo de resposta
- Erro de validação sempre retorna um status code 400 com ProblemDetails

## Convenções
- Nomes de classes e métodos em inglês, mensagens de erro em português
- Commit no formato: tipo(escopo): descrição, com identificador do requisito