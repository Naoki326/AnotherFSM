set windows-shell := ["powershell.exe", "-NoLogo", "-Command"]

api_port := "5079"
frontend_port := "5174"
wasm_port := "5180"

# 编译 API 项目
build-api:
    dotnet build demos/FSMDemo.API/FSMDemo.API.csproj /p:NuGetAudit=false

# 启动 API 服务
api:
    dotnet run --project demos/FSMDemo.API/FSMDemo.API.csproj --urls http://localhost:{{api_port}}

# 启动前端开发服务器
[windows]
frontend:
    npm.cmd run dev --prefix demos/FSMDemo.Frontend

[unix]
frontend:
    npm run dev --prefix demos/FSMDemo.Frontend

# 编译并启动 API
start-api: build-api api

# 同时启动 API 和前端
[windows]
dev: build-api
    Start-Process -FilePath "dotnet" -ArgumentList "run","--project","demos/FSMDemo.API/FSMDemo.API.csproj","--urls","http://localhost:{{api_port}}" -WorkingDirectory (Get-Location) -WindowStyle Minimized
    npm.cmd run dev --prefix demos/FSMDemo.Frontend -- --host 127.0.0.1 --port {{frontend_port}}

[unix]
dev: build-api
    dotnet run --project demos/FSMDemo.API/FSMDemo.API.csproj --urls http://localhost:{{api_port}} &
    npm run dev --prefix demos/FSMDemo.Frontend -- --host 127.0.0.1 --port {{frontend_port}}

# 清理 API 构建产物
clean:
    dotnet clean demos/FSMDemo.API/FSMDemo.API.csproj

# 编译前端
[windows]
build-frontend:
    npm.cmd install --prefix demos/FSMDemo.Frontend
    npm.cmd run build --prefix demos/FSMDemo.Frontend

[unix]
build-frontend:
    npm install --prefix demos/FSMDemo.Frontend
    npm run build --prefix demos/FSMDemo.Frontend

# 启动浏览器内 WASM 示例
wasm:
    dotnet run --project demos/FSMDemo.Wasm/FSMDemo.Wasm.csproj --urls http://localhost:{{wasm_port}}

# 发布浏览器内 WASM 示例，用于 GitHub Pages
build-wasm:
    dotnet publish demos/FSMDemo.Wasm/FSMDemo.Wasm.csproj -c Release -o release --nologo /p:NuGetAudit=false
