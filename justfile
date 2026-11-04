set shell := ["bash", "-uc"]
set windows-shell := ["bash", "-uc"]

api_port := "5079"
frontend_port := "5174"

# 核心引擎回归
test:
    dotnet test tests/StateMachineTest/StateMachineTest.csproj --nologo

# 共享图编辑与模块导入导出回归（含 API 控制器）
test-api:
    dotnet test tests/FSMDemoApiTest/FSMDemoApiTest.csproj --nologo

# 宿主 .NET 运行共享浏览器会话合同，不代表真实 WASM 兼容性
test-wasm:
    dotnet test tests/FSMDemoWasmTest/FSMDemoWasmTest.csproj --nologo

build-api:
    dotnet build demos/FSMDemo.API/FSMDemo.API.csproj -p:NuGetAudit=false

api:
    dotnet run --project demos/FSMDemo.API/FSMDemo.API.csproj --urls http://localhost:{{api_port}}

frontend:
    VITE_RUNTIME_MODE=api npm run dev --prefix demos/FSMDemo.Frontend

start-api: build-api api

# 两个子进程退出时清理；后台 API 输出留档
dev: build-api
    #!/usr/bin/env bash
    set -euo pipefail
    mkdir -p .scratch
    dotnet run --no-build --project demos/FSMDemo.API/FSMDemo.API.csproj --urls http://localhost:{{api_port}} > .scratch/dev-api.log 2>&1 &
    api_pid=$!
    trap 'kill "$api_pid" 2>/dev/null || true' EXIT
    VITE_RUNTIME_MODE=api npm run dev --prefix demos/FSMDemo.Frontend -- --host 127.0.0.1 --port {{frontend_port}}

clean:
    dotnet clean demos/FSMDemo.API/FSMDemo.API.csproj
    dotnet clean demos/FSMDemo.Browser/FSMDemo.Browser.csproj

build-frontend:
    npm ci --prefix demos/FSMDemo.Frontend
    npm run build --prefix demos/FSMDemo.Frontend

# 静态发布入口只有 React + .NET WASM，无旧 Blazor 应用
build-wasm:
    bash tools/build-browser.sh

wasm: build-wasm
    node tools/serve-browser.mjs

# 先验证 API 未运行的 WASM，再启动 API 运行同一合同
test-browser:
    RUNTIME_CONTRACT=true bash tools/build-browser.sh
    node tools/run-browser-tests.mjs
