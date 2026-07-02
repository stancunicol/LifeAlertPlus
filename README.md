# LifeAlertPlus

Short description
-----------------
LifeAlertPlus is a monitoring and alerting platform specifically designed to help monitor and protect elderly people. It combines a .NET backend, a Blazor web client, device firmware (ESP32), and an experimental AI microservice to provide real-time detection, notifications, incident management and historical analysis for at-risk individuals.

Key functionalities (features)
------------------------------
- Real-time event collection from devices and external sources.
- Rule-based detection and severity classification (time windows, multi-metric conditions).
- Flexible alerting: email, webhooks, and extensible notification channels with throttling, deduplication, escalation and retry policies.
- Incident lifecycle: acknowledge, comment, resolve, and audit trail for all actions.
- Web dashboard (Blazor) for live monitoring, timelines and trend charts.
- Historical storage and retention policies for reports and forensics.
- Extensible plugin/adapter architecture for sensors and notification providers.
- Optional high-performance native modules for latency-sensitive processing.
- Python utilities and an AI microservice for ML-assisted analytics and anomaly detection.
- Device/firmware support (ESP32) for on-device sensing and edge processing.

Project structure (exact, readable)
-----------------------------------
root
- .github/  
  GitHub actions and repo configurations

- .dockerignore

- .gitignore

- lifealertplus.code-workspace

- VERSION

- LifeAlertPlus/  
  .NET solution and C# projects
  - LifeAlertPlus.sln

  - LifeAlertPlus.API/  
    ASP.NET Core backend (HTTP + SignalR)
    - Program.cs                       (app entry, DI and host configuration)
    - LifeAlertPlus.API.csproj
    - appsettings.json
    - appsettings.Development.json
    - appsettings.Production.json
    - LifeAlertPlus.API.http
    - Controllers/                      (REST controllers)
    - Services/                         (API-specific services & handlers)
    - Hubs/                             (SignalR hubs)
    - wwwroot/                          (static files if client is hosted)

  - LifeAlertPlus.Application/  
    Application layer (use cases, orchestration services)

  - LifeAlertPlus.Domain/  
    Domain models and interfaces
    - Entities/
    - IRepositories/
    - LifeAlertPlus.Domain.csproj

  - LifeAlertPlus.Infrastructure/  
    Persistence, EF Core context, repositories and seed data
    - Context/
    - Repositories/
    - Services/
    - Seed/
    - LifeAlertPlus.Infrastructure.csproj

  - LifeAlertPlus.Client/  
    Blazor client (UI)
    - App.razor
    - Program.cs
    - LifeAlertPlus.Client.csproj
    - Components/
    - Pages/
    - Layout/
    - wwwroot/

  - LifeAlertPlus.Shared/  
    Shared DTOs and helpers

  - LifeAlertPlus.Tests/  
    Unit & integration tests

- ai-service/  
  Python ML/AI microservice and artifacts
  - main.py
  - requirements.txt
  - Dockerfile
  - models/
  - patient_monitor_artifacts*.joblib

- firmware/  
  ESP32 firmware and board configs
  - CMakeLists.txt
  - sdkconfig.defaults
  - partitions.csv
  - LifeAlertPlusESP32.sln
  - README.md
  - main/   (firmware source files)
  - .clangd
  - .gitignore

How it fits together
--------------------
- Devices run firmware (firmware/) and send sensor data to the backend API (LifeAlertPlus.API).
- The API applies domain logic (Application, Domain, Infrastructure), persists events, evaluates detection rules, and emits alerts and SignalR notifications.
- The Blazor client (LifeAlertPlus.Client) consumes API and SignalR endpoints to provide dashboards and incident management.
- The ai-service can run separately to provide ML/analytics and assist detection or prioritization.
- The architecture supports adapters/plugins for new sensors and notification providers.

Quick start (shortest path)
---------------------------
1. Clone repository:
   git clone https://github.com/stancunicol/LifeAlertPlus.git
   cd LifeAlertPlus

2. Build & run the backend:
   dotnet restore LifeAlertPlus/LifeAlertPlus.sln
   dotnet build LifeAlertPlus/LifeAlertPlus.sln
   dotnet run --project LifeAlertPlus/LifeAlertPlus.API

3. Run the Blazor client (if not hosted by the API):
   dotnet run --project LifeAlertPlus/LifeAlertPlus.Client

4. Optional: AI microservice
   python -m venv .venv
   source .venv/bin/activate   # Windows: .venv\Scripts\activate
   pip install -r ai-service/requirements.txt
   python ai-service/main.py

5. Firmware
   Inspect firmware/ for build instructions (CMake/ESP-IDF).

Configuration
-------------
- Backend configuration: LifeAlertPlus/LifeAlertPlus.API/appsettings*.json and environment variables (ports, DB connection strings, notification credentials).
- AI service: ai-service/main.py and environment/config variables; Dockerfile included.
- Device/firmware defaults: firmware/sdkconfig.defaults and partitions.csv.

Testing
-------
- C#: dotnet test against LifeAlertPlus.Tests projects.

Contributing
------------
1. Fork, create a branch per feature/fix.
2. Add tests and keep changes focused.
3. Open a PR describing the change and update docs if behavior/API changes.
