# API Regression Lab（接口差异回归实验室）

用**同一批请求**分别调用**基线服务**和**候选服务**，对两边的
**状态码、响应头、JSON 内容、响应时间**做结构化比较，并保存每一次运行。

- 后端：ASP.NET Core 8 + PostgreSQL（EF Core，启动自动迁移 + 种子）
- 前端：React 18 + TypeScript + Vite（构建后由后端托管，单端口交付）
- 目标服务：两个内置的可控 fixture（同一镜像，`FIXTURE_MODE` 切换人格）
- 一键启动：`docker compose up --build`

## 快速开始

```bash
docker compose up --build
# 打开 http://localhost:8080
```

启动后：

1. `postgres` 健康检查通过后，`api` 自动执行 EF Core 迁移并写入示例数据；
2. 自带 **10 个示例场景**，开箱即跑出 **4 个无差异（Match）+ 6 个有差异（Diff）**；
3. 前端"运行"页点 **▶ 启动运行**，可实时看进度、展开任意一条 diff。

重新来一套干净数据：`docker compose down -v && docker compose up --build`。

### 运行全部测试

```bash
# 后端 xUnit（规则 / 并发与取消 / 敏感值 / 服务不可达），46 个
docker compose --profile test run --rm backend-tests

# 浏览器端主流程（Playwright，7 个）
docker compose --profile test run --rm e2e
```

本地非容器方式：

```bash
dotnet test tests/RegressionLab.Tests
cd web && npm install && npm run dev        # UI 开发，代理到 :8080
cd tests/e2e && npm install && npx playwright install chromium && npx playwright test
```

## 场景（请求）能力

路径参数、查询参数、请求头、JSON body 都可编辑：

- 路径里写 `{id}`，在"路径参数"里给值；查询参数可写在路径的 `?` 后，也可单独维护；
- 所有字段都支持密钥占位符 **`{{secret:NAME}}`**；
- **密钥值只来自服务端环境变量**，数据库和页面里只保存/显示占位符引用本身；
  - 提交场景时若把某个环境变量的**明文值**直接粘进任何字段，后端会拒绝保存；
  - 运行时从环境变量解析；目标响应（头/body/错误文本）中出现的明文密钥在入库前会被替换为 `***`，页面永不回显；
  - compose 里的演示密钥是 `RLAB_SECRET_DEMO_API_KEY`（见 `docker-compose.yml`，生产请换成真实密钥）。

## 比较规则与版本化

规则集可维护：

- **忽略 JSON 路径**：`$.meta.request_id`、`$.items[*].ts`（支持属性/索引/`*` 通配，含子树）；
- **数组按键排序**：`$.items → id`，先排序再比较，顺序差异不再误报；
- **浮点容差**：绝对容差与相对容差，`|a-b| ≤ max(absTol, relTol·max(|a|,|b|))`；
- **动态值正则屏蔽**：`$.generatedAt → \d{4}-\d{2}-…`，**两边都匹配**正则即视为相等；
- **忽略响应头**（`date`、`server`、`content-length` 等 hop-by-hop 头默认忽略）；
- 响应时间在绝对与相对阈值**同时**超出时才报 diff。

**版本化语义**：再次保存同名规则集会创建一个新版本行，旧版本冻结；场景随规则族迁到新版本，
而**每次运行启动时把规则完整快照（含版本号）存进行记录**——旧运行永远保留它当时使用的版本，
事后改规则不会改变历史结论。

## 运行控制

启动一次运行时可配置：

- **任务并发数**（同时处理多少个场景，每个场景并发打基线+候选）；
- **单请求超时**（毫秒）；
- **每个目标各自的速率限制**（rps，0 = 不限）。

**取消**：取消后不会再启动任何新请求；已经完成的场景结果全部保留，运行标记为 `Cancelled`。

## 网络失败 ≠ 空响应

目标不可达是独立的结果类别 `NetworkFailure` 和独立 diff 区域 `Reachability`：

- `connect_refused`（连接被拒）、`dns_error`、`timeout`（单请求超时）、
  `network_unreachable`、`tls_error`、`transport_error`；
- 失败一侧没有状态码、没有响应体，绝不会被伪装成"200 空响应"去和另一边比内容；
- 另一侧真实收到的响应照常展示。

## 自带 fixture 的差异点

`fixtures/RegressionLab.Fixture` 一份代码，靠 `FIXTURE_MODE` 变成 baseline / candidate：

| 场景 | 差异 |
| --- | --- |
| health / user by id / search / echo | 完全一致（时间戳与 request_id 被规则忽略/屏蔽） |
| POST /users | 状态码 200 vs **201** |
| GET /users/7 | 响应头 `X-Rate-Limit` 100 vs 200 |
| GET /users/9 | JSON `$.email` 值不同 |
| GET /score | `$.score` 数值漂移超出容差 |
| GET /orders | 数组按 id 排序后，`$.items[2].amount` 不同 |
| GET /secure/whoami | 候选密钥不同 → 候选 **401** |

`GET /slow/{ms}` 用于取消/超时测试。

## HTTP API 摘要

| 方法 & 路径 | 说明 |
| --- | --- |
| `GET /api/config` | 默认基线/候选地址、可用密钥名（只有名字，没有值） |
| `GET/POST /api/rules` · `GET /api/rules/{id}` | 规则集（POST 同名 = 新版本） |
| `GET/POST /api/scenarios` · `PUT/DELETE /api/scenarios/{id}` | 场景增删改查 |
| `POST /api/runs` | 启动运行（body 可覆盖目标地址与并发/超时/限速） |
| `GET /api/runs` · `GET /api/runs/{id}` | 运行列表 / 进度 |
| `POST /api/runs/{id}/cancel` | 取消（不再启动新请求，保留已完成结果） |
| `GET /api/runs/{id}/results` | 每条场景的双侧响应与结构化 diff |

## 目录结构

```
src/RegressionLab.Api/      ASP.NET Core 后端（领域模型 / 比较引擎 / 执行器 / 最小 API / 迁移）
  Comparison/               JSON 比较：路径、排序、容差、正则屏蔽、规则快照
  Execution/                限速门、HTTP 调用与错误分类、响应比较、并发/取消执行器
  Security/                 密钥解析、明文拦截、响应脱敏
  Data/Migrations/          EF Core InitialCreate
fixtures/RegressionLab.Fixture/  一个镜像两种人格的示例目标
web/                        React 前端（场景 / 规则 / 运行三个页面）
tests/RegressionLab.Tests/  xUnit：规则、并发与取消、敏感值、不可达（46 个）
tests/e2e/                  Playwright 浏览器主流程（7 个）
docker-compose.yml          postgres + baseline + candidate + api（+ test profile）
```
