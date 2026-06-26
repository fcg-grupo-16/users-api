# FCG Users API

Microsserviço de **usuários** da plataforma FIAP Cloud Games (FCG) — Fase 2 (decomposição do monolito em microsserviços).

## Propósito

Responsável por:

- **Cadastro** de usuários (registro, atualização, desativação por soft delete).
- **Autenticação** via JWT (login com e-mail e senha).
- **Autorização** baseada em papéis (`Usuario` e `Administrador`).
- **Publicação de evento**: ao cadastrar um novo usuário, publica `UserCreatedEvent` (namespace `Fcg.Contracts.Events`) no RabbitMQ via MassTransit. O `NotificationsAPI` consome este evento para o e-mail de boas-vindas.

Construído em **Clean Architecture** com .NET 10, MongoDB e MassTransit/RabbitMQ.

## Arquitetura

```
src/
  Fcg.Users.Domain          — Entidade Usuario, VOs (Email, Senha), enum TipoUsuario, exceções, IUsuarioRepository
  Fcg.Users.Application      — DTOs, serviços, validators, IEventPublisher, Contracts/Events.cs
  Fcg.Users.Infrastructure   — AppDbContext (MongoDB), repositório, JWT, BCrypt, MassTransitEventPublisher, seed
  Fcg.Users.Api              — Controllers, middlewares, Program.cs
tests/
  Fcg.Users.UnitTests        — Testes de domínio, serviços e validators
```

## Endpoints

| Método | Rota                       | Autorização          | Descrição                              |
| ------ | -------------------------- | -------------------- | -------------------------------------- |
| POST   | `/api/v1/auth/login`       | Anônimo              | Autenticar e obter token JWT           |
| POST   | `/api/v1/usuarios`         | Anônimo              | Registrar novo usuário                 |
| GET    | `/api/v1/usuarios`         | `Administrador`      | Listar usuários (paginado)             |
| GET    | `/api/v1/usuarios/{id}`    | Autenticado (próprio ou admin) | Obter usuário por ID         |
| PUT    | `/api/v1/usuarios/{id}`    | Autenticado (próprio ou admin) | Atualizar usuário            |
| DELETE | `/api/v1/usuarios/{id}`    | `Administrador`      | Desativar usuário (soft delete)        |
| GET    | `/health`                  | Anônimo              | Health check (liveness/readiness k8s)  |

## Variáveis de ambiente

Todas as chaves de configuração podem ser sobrescritas por variáveis de ambiente usando o separador de duplo sublinhado (`__`).

| Variável                              | Padrão                  | Descrição                                  |
| ------------------------------------- | ----------------------- | ------------------------------------------ |
| `ASPNETCORE_ENVIRONMENT`              | `Production`            | Ambiente de execução                       |
| `ASPNETCORE_URLS`                     | `http://+:8080`         | URL de escuta (definida no Dockerfile)     |
| `MongoDbSettings__ConnectionString`   | `mongodb://localhost:27017` | String de conexão do MongoDB           |
| `MongoDbSettings__DatabaseName`       | `usersdb`               | Nome do banco de dados                     |
| `JwtSettings__SecretKey`              | (dev placeholder)       | Chave secreta HMAC-SHA256 (≥ 256 bits)     |
| `JwtSettings__Issuer`                 | `FiapCloudGames`        | Emissor do token                           |
| `JwtSettings__Audience`               | `FiapCloudGames`        | Audiência do token                         |
| `JwtSettings__ExpiracaoEmMinutos`     | `30`                    | Tempo de expiração do token (minutos)      |
| `RabbitMq__Host`                      | `localhost`             | Host do RabbitMQ                           |
| `RabbitMq__Username`                  | `guest`                 | Usuário do RabbitMQ                        |
| `RabbitMq__Password`                  | `guest`                 | Senha do RabbitMQ                          |

## Como executar

### Localmente (`dotnet run`)

Pré-requisitos: .NET 10 SDK, MongoDB e RabbitMQ acessíveis.

```bash
dotnet restore
dotnet run --project src/Fcg.Users.Api
```

A API estará disponível em `http://localhost:8080` (ou conforme `ASPNETCORE_URLS`). Em `Development`, o Swagger fica em `/swagger`.

### Docker

```bash
docker build -t users-api:local .
docker run -p 8080:8080 \
  -e MongoDbSettings__ConnectionString="mongodb://host.docker.internal:27017" \
  -e RabbitMq__Host="host.docker.internal" \
  users-api:local
```

### Kubernetes

```bash
kubectl apply -f k8s/
```

## Testes

```bash
dotnet test
```

## Credenciais de administrador (seed)

Na primeira inicialização, um usuário administrador é criado automaticamente (a menos que já exista):

- **E-mail:** `admin@fcg.com`
- **Senha:** `Admin@123456`
