# UsersAPI — FIAP Cloud Games

Microsserviço de cadastro de usuários, autenticação (JWT) e autorização da plataforma FIAP Cloud Games (FCG) — Fase 2.

![CI](https://github.com/fcg-grupo-16/users-api/actions/workflows/ci.yml/badge.svg)

---

## 1. Visão geral

O **UsersAPI** é responsável por:

- **Cadastro de usuários** — registro e gerenciamento (CRUD) de contas.
- **Autenticação** — login com emissão de token **JWT** (HMAC-SHA256).
- **Autorização** — controle de acesso por papéis (`Usuario` e `Administrador`).

Sempre que um novo usuário é criado, o serviço **publica** o evento **`UserCreatedEvent`** (namespace compartilhado `Fcg.Contracts.Events`) no RabbitMQ. Esse evento é consumido por outros microsserviços do ecossistema — por exemplo, o `NotificationsAPI`, que envia o e-mail de boas-vindas.

**Campos do `UserCreatedEvent`:**

| Campo    | Tipo     | Descrição                          |
|----------|----------|------------------------------------|
| `UserId` | `string` | Identificador do usuário criado.   |
| `Nome`   | `string` | Nome do usuário.                   |
| `Email`  | `string` | E-mail do usuário.                 |

> **Importante:** o namespace e o nome do tipo (`Fcg.Contracts.Events:UserCreatedEvent`) precisam ser **idênticos** em todos os serviços. O MassTransit identifica a mensagem pela URN derivada de `namespace:NomeDoTipo` (ex.: `urn:message:Fcg.Contracts.Events:UserCreatedEvent`); qualquer divergência quebra a interoperabilidade.

---

## 2. Stack

- **.NET 10** (SDK `10.0.100`)
- **Clean Architecture** (monolito modular em 4 projetos)
- **MongoDB** (database `usersdb`) via EF Core Provider para MongoDB
- **RabbitMQ** + **MassTransit 8.x** (publicação de eventos)
- **Serilog** (logs estruturados em JSON no console)
- **FluentValidation** (validação de entrada)
- **JWT** com **HMAC-SHA256** (autenticação)
- **BCrypt** (hash de senhas)
- **Swagger / OpenAPI** (somente em Development)
- **xUnit** (testes unitários)

---

## 3. Arquitetura

Estrutura de pastas (top 3 níveis):

```
users-api/
├── src/
│   ├── Fcg.Users.Domain/          # Entidades, Value Objects, Enums, Exceptions, Repositories (interfaces)
│   ├── Fcg.Users.Application/      # Services (casos de uso), DTOs, Validators, Interfaces, Contracts (eventos)
│   ├── Fcg.Users.Infrastructure/   # MongoDB (Persistence/Repositories), JWT, BCrypt, Messaging, Seed
│   └── Fcg.Users.Api/              # Controllers, Middlewares, Extensions (DI), Program.cs
├── tests/
│   └── Fcg.Users.UnitTests/        # Testes de Entities, Services, Validators, ValueObjects
├── k8s/                            # Manifests Kubernetes
├── Dockerfile
├── UsersApi.sln
└── global.json
```

### Papel de cada projeto

| Projeto                       | Responsabilidade                                                                                          |
|-------------------------------|-----------------------------------------------------------------------------------------------------------|
| `Fcg.Users.Domain`            | Núcleo do negócio: entidades (`Usuario`), value objects (`Email`, `Senha`), enums, exceções e interfaces de repositório. Não depende de nenhuma outra camada. |
| `Fcg.Users.Application`       | Casos de uso (`AuthService`, `UsuarioService`), DTOs de request/response, validadores e os contratos de eventos (`UserCreatedEvent`). |
| `Fcg.Users.Infrastructure`    | Implementações concretas: persistência MongoDB, geração de token JWT, hash BCrypt, publicação de mensagens (MassTransit) e seed inicial. |
| `Fcg.Users.Api`               | Camada de entrada HTTP: controllers, middlewares (correlation id, tratamento global de exceções), composição de DI e bootstrap (`Program.cs`). |

### Publicação de eventos: `IEventPublisher` → `MassTransitEventPublisher`

A camada **Application** depende apenas da abstração `IEventPublisher`:

```csharp
public interface IEventPublisher
{
    Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class;
}
```

A **Infrastructure** fornece a implementação concreta `MassTransitEventPublisher`, que delega para o `IPublishEndpoint` do MassTransit:

```csharp
public sealed class MassTransitEventPublisher(IPublishEndpoint publishEndpoint) : IEventPublisher
{
    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class =>
        publishEndpoint.Publish(message, ct);
}
```

Dessa forma, a Application publica eventos (como o `UserCreatedEvent`) sem conhecer RabbitMQ ou MassTransit — invertendo a dependência (princípio DIP da Clean Architecture). O binding ocorre em `AddInfrastructureServices`:

```csharp
services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
```

### Transactional Outbox com MongoDB

Para evitar o problema de *dual-write* entre MongoDB e RabbitMQ, o cadastro de usuário usa o **MongoDB Outbox** oficial do MassTransit (`MassTransit.MongoDb`).

O fluxo de criação funciona assim:

1. o `UsuarioService` adiciona o usuário sem salvar imediatamente;
2. o `IPublishEndpoint` publica o `UserCreatedEvent` no mesmo escopo do request;
3. o repositório inicia uma transação MongoDB usando `MongoDbContext` do MassTransit;
4. o documento do usuário é inserido na coleção `usuarios` usando a mesma sessão;
5. o commit da transação confirma **o usuário e a mensagem de outbox**;
6. o delivery service do MassTransit publica a mensagem pendente no RabbitMQ.

Com isso, se o broker estiver indisponível no momento do cadastro, o usuário continua sendo persistido e a mensagem fica armazenada nas coleções de outbox (`outbox.messages` e `outbox.states`) até a reconexão do RabbitMQ.

> Implementação importante: no cenário MongoDB, a confirmação da unidade transacional **não** acontece via `DbContext.SaveChanges()` do provider EF do Mongo. O commit é feito pela transação do `MongoDbContext` do MassTransit.

---

## 4. Pré-requisitos

- **.NET 10 SDK** (`10.0.100` ou superior compatível)
- **Docker** (para subir MongoDB e RabbitMQ localmente e/ou para empacotar a API)

### Subir as dependências localmente (Docker)

**MongoDB:**

```bash
docker run -d --name fcg-mongo -p 27017:27017 mongo:7 --replSet rs0 --bind_ip_all
docker exec fcg-mongo mongosh --eval "rs.initiate({_id:'rs0',members:[{_id:0,host:'host.docker.internal:27017'}]})"
```

> O UsersAPI usa o MongoDB Outbox oficial do MassTransit (`MassTransit.MongoDb`) com `AddMongoDbOutbox(...)` e `UseBusOutbox()`, conforme a documentacao do framework para MongoDB.
>
> Para cenarios que dependem de transacoes MongoDB, valide o ambiente com replica set habilitado. Em standalone, a API pode iniciar normalmente, mas garantias transacionais mais fortes dependem do suporte do ambiente Mongo.

**RabbitMQ (com painel de administração em http://localhost:15672 — guest/guest):**

```bash
docker run -d --name fcg-rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

---

## 5. Variáveis de ambiente

A configuração padrão vive em `src/Fcg.Users.Api/appsettings.json`. Qualquer chave pode ser sobrescrita por variável de ambiente usando o separador `__` (duplo underscore) no formato `Secao__Chave`.

| Variável                              | Descrição                                                       | Default                                  |
|---------------------------------------|-----------------------------------------------------------------|------------------------------------------|
| `ASPNETCORE_ENVIRONMENT`              | Ambiente de execução (`Development` habilita o Swagger).        | `Production` (no container)              |
| `MongoDbSettings__ConnectionString`   | String de conexão do MongoDB.                                   | `mongodb://localhost:27017/?replicaSet=rs0` |
| `MongoDbSettings__DatabaseName`       | Nome do database.                                               | `usersdb`                                |
| `JwtSettings__SecretKey`              | Chave secreta para assinar/validar o JWT (mín. 256 bits para HMAC-SHA256). **Deve ser idêntica à dos demais serviços que validam o token.** | `OVERRIDE_VIA_ENV_VAR_EM_PRODUCAO`       |
| `JwtSettings__Issuer`                 | Emissor do token.                                               | `FiapCloudGames`                         |
| `JwtSettings__Audience`               | Audiência do token.                                             | `FiapCloudGames`                         |
| `JwtSettings__ExpiracaoEmMinutos`     | Tempo de expiração do token, em minutos.                        | `30`                                     |
| `RabbitMq__Host`                      | Host do RabbitMQ.                                               | `localhost`                              |
| `RabbitMq__Username`                  | Usuário do RabbitMQ.                                            | `guest`                                  |
| `RabbitMq__Password`                  | Senha do RabbitMQ.                                              | `guest`                                  |

> **Nunca** comite segredos reais (chaves JWT de produção, senhas, strings de conexão com credenciais) no repositório.

---

## 6. Como rodar localmente (dotnet)

1. **Suba as dependências** (MongoDB e RabbitMQ) conforme a seção [Pré-requisitos](#4-pré-requisitos).

2. **Restaure e execute a API:**

   ```bash
   dotnet restore
   dotnet run --project src/Fcg.Users.Api
   ```

3. **Acesse a API.** A URL é exibida no console na inicialização (tipicamente `http://localhost:5xxx`). Em ambiente **Development**, o Swagger fica disponível em:

   ```
   http://localhost:5xxx/swagger
   ```

   > O Swagger só é habilitado quando `ASPNETCORE_ENVIRONMENT=Development`. Para garantir o ambiente:
   >
   > ```bash
   > ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Fcg.Users.Api
   > ```

4. **Health check:** `GET /health`.

### Credenciais do admin semeado

Na inicialização, o serviço cria automaticamente um usuário administrador (se ainda não existir):

| Campo   | Valor             |
|---------|-------------------|
| E-mail  | `admin@fcg.com`   |
| Senha   | `Admin@123456`    |

Use essas credenciais no endpoint `POST /api/v1/auth/login` para obter um token JWT de administrador.

---

## 7. Como rodar com Docker

1. **Build da imagem:**

   ```bash
   docker build -t users-api:local .
   ```

2. **Run do container** (porta `8081` do host → `8080` do container). Aponte o Mongo e o RabbitMQ para os contêineres de dependência (use `host.docker.internal` se eles rodarem no host):

   ```bash
   docker run -d --name users-api -p 8081:8080 \
     -e ASPNETCORE_ENVIRONMENT=Development \
       -e MongoDbSettings__ConnectionString="mongodb://host.docker.internal:27017/?replicaSet=rs0" \
     -e MongoDbSettings__DatabaseName="usersdb" \
     -e JwtSettings__SecretKey="FiapCloudGames_Demo_SecretKey_Com_Pelo_Menos_256_Bits_Para_HMAC_SHA256!" \
     -e RabbitMq__Host="host.docker.internal" \
     -e RabbitMq__Username="guest" \
     -e RabbitMq__Password="guest" \
     users-api:local
   ```

   A API ficará acessível em `http://localhost:8081` (Swagger em `http://localhost:8081/swagger`, pois `ASPNETCORE_ENVIRONMENT=Development`).

> O container expõe a porta **8080** internamente (`ASPNETCORE_URLS=http://+:8080`).

---

## 8. Rodar o ecossistema completo (end-to-end)

O FCG Fase 2 é composto por **5 microsserviços** que se comunicam via RabbitMQ. Para subir tudo de uma vez, use o repositório de orquestração.

1. Clone os 5 repositórios como **irmãos** (no mesmo diretório pai), pois o `docker compose` os referencia por caminho relativo:

   ```bash
   git clone https://github.com/fcg-grupo-16/users-api.git
   git clone https://github.com/fcg-grupo-16/catalog-api.git
   git clone https://github.com/fcg-grupo-16/payments-api.git
   git clone https://github.com/fcg-grupo-16/notifications-api.git
   git clone https://github.com/fcg-grupo-16/orchestration.git
   ```

2. Suba o ecossistema a partir do repositório de orquestração:

   ```bash
   cd orchestration
   docker compose up
   ```

Detalhes e a lista exata de serviços ficam em: **https://github.com/fcg-grupo-16/orchestration**

---

## 9. Endpoints

Base path: `/api/v1`. Documentação interativa via Swagger (em Development).

### Autenticação — `/api/v1/auth`

| Método | Rota                  | Auth        | Descrição                                              |
|--------|-----------------------|-------------|--------------------------------------------------------|
| POST   | `/api/v1/auth/login`  | Pública     | Autentica o usuário e retorna o token JWT + expiração. |

### Usuários — `/api/v1/usuarios`

| Método | Rota                     | Auth                                  | Descrição                                                    |
|--------|--------------------------|---------------------------------------|--------------------------------------------------------------|
| POST   | `/api/v1/usuarios`       | Pública                               | Registra um novo usuário. Publica `UserCreatedEvent`.        |
| GET    | `/api/v1/usuarios`       | **Administrador**                     | Lista usuários com paginação (`pagina`, `tamanhoPagina`).    |
| GET    | `/api/v1/usuarios/{id}`  | Autenticado (próprio usuário ou Admin)| Obtém um usuário por ID.                                     |
| PUT    | `/api/v1/usuarios/{id}`  | Autenticado (próprio usuário ou Admin)| Atualiza nome e e-mail do usuário.                           |
| DELETE | `/api/v1/usuarios/{id}`  | **Administrador**                     | Desativa o usuário (soft delete).                            |

> **Regras de autorização:**
> - `ApenasAdmin` → exige o papel `Administrador` (listar e remover).
> - `UsuarioAutenticado` → exige usuário autenticado; além disso, em `GET {id}` e `PUT {id}` o usuário só pode acessar **o próprio recurso**, exceto se for `Administrador` (caso contrário, `403 Forbidden`).

### Exemplos

**Login:**

```bash
curl -X POST http://localhost:8081/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@fcg.com","senha":"Admin@123456"}'
```

**Criar usuário:**

```bash
curl -X POST http://localhost:8081/api/v1/usuarios \
  -H "Content-Type: application/json" \
  -d '{"nome":"João Silva","email":"joao@exemplo.com","senha":"Senha@123456"}'
```

**Listar usuários (token de admin):**

```bash
curl "http://localhost:8081/api/v1/usuarios?pagina=1&tamanhoPagina=10" \
  -H "Authorization: Bearer <SEU_TOKEN_JWT>"
```

> No Swagger, informe **apenas o token JWT** (sem o prefixo `Bearer`) no botão *Authorize*.

---

## 10. Testes

Execute toda a suíte de testes unitários:

```bash
dotnet test
```

A suíte (`tests/Fcg.Users.UnitTests`) cobre:

- **Entities** — regras de negócio da entidade `Usuario`.
- **Services** — casos de uso `AuthService` (login/JWT) e `UsuarioService` (CRUD e publicação de evento).
- **Validators** — `CriarUsuarioValidator`, `AtualizarUsuarioValidator` e `LoginValidator`.
- **ValueObjects** — `Email` e `Senha`.

### Teste manual de resiliência do outbox

Com o ecossistema completo em execução e o MongoDB em replica set, o comportamento esperado é:

1. parar o RabbitMQ;
2. cadastrar um usuário com `POST /api/v1/usuarios`;
3. receber `201 Created` mesmo sem broker;
4. observar no MongoDB documentos temporários em `outbox.messages` e `outbox.states`;
5. religar o RabbitMQ;
6. observar o `NotificationsAPI` consumir o `UserCreatedEvent` e registrar o e-mail de boas-vindas;
7. confirmar que `outbox.messages` e `outbox.states` voltaram para zero.

Exemplo usando o ambiente de orquestração:

```bash
docker stop fcg-rabbitmq

curl -i -X POST http://localhost:8081/api/v1/usuarios \
   -H "Content-Type: application/json" \
   -d '{"nome":"Maria","email":"maria@email.com","senha":"Senha@123"}'

docker start fcg-rabbitmq
docker logs fcg-notifications-api --tail 50
docker exec fcg-mongodb mongosh "mongodb://localhost:27017/usersdb?directConnection=true" --quiet --eval "db.getCollectionNames().forEach(c => print(c + ':' + db.getCollection(c).countDocuments()))"
```

---

## 11. Como contribuir

1. **Pegue/assigne uma issue** antes de começar.
2. **Crie um branch** a partir de `main`, no padrão:
   - `feat/<n>-descricao` para novas funcionalidades
   - `fix/<n>-descricao` para correções

   (onde `<n>` é o número da issue)

   ```bash
   git checkout -b feat/42-cadastro-com-telefone
   ```

3. **Use Conventional Commits** nas mensagens:

   ```
   feat: adiciona campo telefone ao cadastro de usuário
   fix: corrige validação de e-mail duplicado
   ```

4. **Abra um Pull Request para `main`**, referenciando a issue no corpo:

   ```
   Closes #42
   ```

5. **Aguarde o CI ficar verde** (build + testes via GitHub Actions).
6. Após aprovação e CI verde, faça o **merge**.

> **Regra obrigatória:** nunca comite segredos reais (chaves, senhas, tokens). Use variáveis de ambiente e os mecanismos de Secret do Kubernetes.

---

## 12. Deploy de versão

O versionamento segue **SemVer**, com tags no formato `vX.Y.Z`.

1. **Build da imagem com a tag de versão:**

   ```bash
   docker build -t ghcr.io/fcg-grupo-16/users-api:vX.Y.Z .
   ```

2. **Autentique no GitHub Container Registry e faça o push:**

   ```bash
   gh auth token | docker login ghcr.io -u <user> --password-stdin
   docker push ghcr.io/fcg-grupo-16/users-api:vX.Y.Z
   ```

3. **Atualize a imagem no Kubernetes.** Edite `k8s/deployment.yaml` (campo `image:`) **ou** use:

   ```bash
   kubectl set image deploy/users-api users-api=ghcr.io/fcg-grupo-16/users-api:vX.Y.Z -n fcg
   ```

4. **Aplique o manifest** (caso tenha editado o YAML):

   ```bash
   kubectl apply -f k8s/deployment.yaml -n fcg
   ```

---

## 13. Kubernetes

Os manifests vivem em `k8s/`:

| Arquivo            | O que faz                                                                                                  |
|--------------------|------------------------------------------------------------------------------------------------------------|
| `deployment.yaml`  | `Deployment` da API (1 réplica, porta `8080`), com `livenessProbe`/`readinessProbe` em `/health` e injeção de config via `envFrom` (ConfigMap + Secret). |
| `service.yaml`     | `Service` do tipo `ClusterIP` que expõe a porta `80` → `targetPort 8080`.                                  |
| `configmap.yaml`   | `ConfigMap` com configuração não sensível (`ASPNETCORE_ENVIRONMENT`, `RabbitMq__Host`, `MongoDbSettings__DatabaseName`, `JwtSettings__Issuer`, `JwtSettings__Audience`). |
| `secret.yaml`      | `Secret` (Opaque) com dados sensíveis (`MongoDbSettings__ConnectionString`, `JwtSettings__SecretKey`, credenciais do RabbitMQ). **Os valores do repositório são apenas de demonstração** — substitua em ambiente real. |

### Aplicar isoladamente

```bash
kubectl apply -f k8s/configmap.yaml -f k8s/secret.yaml -f k8s/deployment.yaml -f k8s/service.yaml -n fcg
```

> O deploy **completo/agregado** (incluindo MongoDB, RabbitMQ e os demais microsserviços) é orquestrado pelo repositório **[orchestration](https://github.com/fcg-grupo-16/orchestration)**. Os manifests deste repositório são voltados ao deploy isolado do UsersAPI.

---

## 14. Troubleshooting

- **RabbitMQ indisponível na inicialização** — o MassTransit é resiliente e **reconecta automaticamente** quando o broker volta. A API sobe normalmente; mensagens são publicadas assim que a conexão é restabelecida. Verifique `RabbitMq__Host`, `RabbitMq__Username` e `RabbitMq__Password`.

- **Token JWT rejeitado por outros serviços** — `JwtSettings__SecretKey` (e `Issuer`/`Audience`) **deve ser idêntica** em todos os serviços que validam o token (ex.: `catalog-api`). Se a chave divergir, a validação falha com `401`.

- **Falha de conexão com o MongoDB** — confira `MongoDbSettings__ConnectionString` e se o MongoDB está acessível. Dentro de um container, use `host.docker.internal` (Docker Desktop) ou o nome do serviço (`mongodb`) no Kubernetes/compose, não `localhost`.

- **Porta / acesso ao container** — a API escuta na porta **8080** dentro do container (`ASPNETCORE_URLS=http://+:8080`). Lembre-se de publicar a porta no `docker run` (ex.: `-p 8081:8080`). No Kubernetes, o `Service` expõe a porta `80` apontando para a `8080`.

- **Swagger não aparece** — o Swagger só é habilitado em **Development**. Defina `ASPNETCORE_ENVIRONMENT=Development`.
