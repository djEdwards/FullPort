# FullPort — Comprehensive Developer & Architecture Guide

<div align="center">

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-14.0-239120?style=for-the-badge&logo=csharp)
![Bootstrap](https://img.shields.io/badge/Bootstrap-5.3-7952B3?style=for-the-badge&logo=bootstrap)
![OAuth](https://img.shields.io/badge/OAuth-1.0a-EB5424?style=for-the-badge&logo=auth0)

*Everything you need to understand, extend, and maintain FullPort.*

</div>

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [Architecture](#2-architecture)
3. [Technology Stack](#3-technology-stack)
4. [Project Structure & File Map](#4-project-structure--file-map)
5. [Backend Deep Dive](#5-backend-deep-dive)
   - [Application Entry Point](#51-application-entry-point-programcs)
   - [Middleware Pipeline](#52-middleware-pipeline)
   - [Controllers](#53-controllers)
   - [Models & Data Structures](#54-models--data-structures)
   - [OAuth 1.0a Implementation](#55-oauth-10a-implementation)
   - [Shared Authentication State](#56-shared-authentication-state)
6. [Frontend Deep Dive](#6-frontend-deep-dive)
   - [SPA Architecture](#61-spa-architecture)
   - [Tab System](#62-tab-system)
   - [JavaScript Function Reference](#63-javascript-function-reference)
   - [Client-Side State Management](#64-client-side-state-management)
   - [Styling & Theming](#65-styling--theming)
7. [Complete API Reference](#7-complete-api-reference)
   - [Portfolio Controller](#71-portfolio-controller)
   - [Rebalance Controller](#72-rebalance-controller)
   - [Market Controller](#73-market-controller)
   - [Analytics Controller](#74-analytics-controller)
8. [Authentication Flow (Step by Step)](#8-authentication-flow-step-by-step)
9. [Rebalancing Engine](#9-rebalancing-engine)
   - [Pre-Built Strategies](#91-pre-built-strategies)
   - [Risk Profile Allocations](#92-risk-profile-allocations)
   - [Trade Generation Logic](#93-trade-generation-logic)
   - [Asset Class Mappings](#94-asset-class-mappings)
10. [Portfolio Builder](#10-portfolio-builder)
11. [Analytics Engine](#11-analytics-engine)
12. [Configuration Reference](#12-configuration-reference)
13. [Development Workflow](#13-development-workflow)
14. [Glossary](#14-glossary)

---

## 1. System Overview

FullPort is a **self-hosted portfolio management platform** that integrates with the **E\*TRADE brokerage API** to provide real-time portfolio analysis, intelligent rebalancing, and automated trade execution. It is a single-user, single-process application designed for individual investors who want institutional-quality portfolio management tools.

### What FullPort Does

| Capability | Description |
|---|---|
| **Authentication** | OAuth 1.0a flow with E\*TRADE — obtain and manage access tokens |
| **Portfolio Dashboard** | Aggregate real-time valuations across all brokerage accounts |
| **Holdings Management** | View, sort, and analyze all positions with detailed metrics |
| **Rebalancing** | 9 pre-built strategies + custom allocation — compute and execute trades |
| **Portfolio Builder** | Construct new positions from scratch using strategy-based recommendations |
| **Analytics** | Portfolio health scores, asset correlation, benchmark comparison |
| **Trade Execution** | Preview and place market/limit orders through E\*TRADE |

### How Data Flows

```
┌──────────────────────────────────────────────────────────────────────────┐
│                              Browser (SPA)                              │
│                                                                         │
│  index.html ─── Bootstrap 5.3 ─── Vanilla JS ─── localStorage          │
│       │                                                                 │
│       │  fetch('/api/...')                                               │
└───────┼─────────────────────────────────────────────────────────────────┘
        │  HTTP (JSON)
        ▼
┌───────────────────────────────────────────────────────────────────┐
│                      ASP.NET Core 10 Web API                      │
│                                                                   │
│  Program.cs  →  Middleware Pipeline  →  Controller Routing         │
│                                                                   │
│  ┌──────────────┐ ┌──────────────┐ ┌────────────┐ ┌───────────┐  │
│  │  Portfolio    │ │  Rebalance   │ │  Market    │ │ Analytics │  │
│  │  Controller   │ │  Controller  │ │  Controller│ │ Controller│  │
│  │              │ │              │ │            │ │           │  │
│  │ • Auth flow  │ │ • Models     │ │ • Quotes   │ │ • Health  │  │
│  │ • Accounts   │ │ • Calculate  │ │ • Indices  │ │ • Correl. │  │
│  │ • Positions  │ │ • Execute    │ │ • ETFs     │ │ • Bench.  │  │
│  │ • Balances   │ │              │ │ • Build    │ │           │  │
│  └──────┬───────┘ └──────┬───────┘ └─────┬──────┘ └─────┬─────┘  │
│         │                │               │              │         │
│         └────────────────┴───────────────┴──────────────┘         │
│                          │                                        │
│              OAuth 1.0a signed HTTP requests                      │
│           (HMAC-SHA1 signature per request)                       │
└──────────────────────────┬────────────────────────────────────────┘
                           │
                           ▼
              ┌─────────────────────────┐
              │     E*TRADE API          │
              │                         │
              │  Production:            │
              │  https://api.etrade.com │
              │                         │
              │  Sandbox:               │
              │  https://apisb.etrade.com│
              │                         │
              │  OAuth endpoints:       │
              │  /oauth/request_token   │
              │  /oauth/access_token    │
              │                         │
              │  Data endpoints:        │
              │  /v1/accounts/...       │
              │  /v1/market/...         │
              │  /v1/orders/...         │
              └─────────────────────────┘
```

---

## 2. Architecture

### Architectural Style

FullPort follows a **monolithic SPA + API** architecture:

- **Backend**: ASP.NET Core Web API with controller-based routing
- **Frontend**: Single HTML file served as static content with vanilla JavaScript
- **External API**: E\*TRADE REST API accessed via OAuth 1.0a signed HTTP requests
- **Data Storage**: None — entirely stateless (in-memory tokens only, no database)

### Key Architectural Decisions

| Decision | Rationale |
|---|---|
| **No database** | All data comes from E\*TRADE in real time; no need to persist |
| **Single HTML file** | Simplicity — no build step, no bundling, no framework overhead |
| **Static token storage** | Single-user design — tokens stored in static fields for session |
| **OAuth 1.0a** | Required by E\*TRADE's API specification |
| **No frontend framework** | Vanilla JS with Bootstrap keeps dependencies minimal |
| **Controller-per-domain** | Clean separation: Portfolio, Rebalance, Market, Analytics |

### Request Lifecycle

1. **Browser** makes `fetch()` call to `/api/{controller}/{action}`
2. **ASP.NET Core** routes the request through the middleware pipeline
3. **Controller** validates authentication (checks static access token)
4. **Controller** builds an OAuth 1.0a signed request to E\*TRADE
5. **E\*TRADE API** returns JSON data
6. **Controller** parses, transforms, and enriches the response
7. **JSON response** is returned to the browser
8. **JavaScript** updates the DOM with the new data

---

## 3. Technology Stack

### Backend

| Component | Technology | Version | Purpose |
|---|---|---|---|
| Runtime | .NET | 10.0 | Application runtime |
| Language | C# | 14.0 | Primary backend language |
| Framework | ASP.NET Core | 10.0 | Web API framework |
| Serialization | System.Text.Json | Built-in | JSON parsing and generation |
| Cryptography | System.Security.Cryptography | Built-in | HMAC-SHA1 for OAuth signatures |
| HTTP Client | System.Net.Http.HttpClient | Built-in | Outbound HTTP to E\*TRADE |
| API Docs | Microsoft.AspNetCore.OpenApi | 10.0.3 | OpenAPI/Swagger (dev only) |

> **Note**: The only external NuGet package is `Microsoft.AspNetCore.OpenApi`. Everything else uses built-in .NET libraries.

### Frontend

| Component | Technology | Version | Source |
|---|---|---|---|
| UI Framework | Bootstrap | 5.3.0 | CDN |
| Icons | Bootstrap Icons | 1.11.0 | CDN |
| JavaScript | Vanilla ES6+ | N/A | Inline in `index.html` |
| Persistence | localStorage | N/A | Browser built-in |

### External Services

| Service | Purpose | Authentication |
|---|---|---|
| E\*TRADE API | Brokerage data, trading | OAuth 1.0a |
| Bootstrap CDN | UI framework delivery | None |

---

## 4. Project Structure & File Map

```
FullPort/                              ← Repository root
├── README.md                          ← User-facing documentation
├── GUIDE.md                           ← This file — developer guide
│
└── FullPort/                          ← .NET project root
    ├── FullPort.csproj                ← Project file (target: net10.0)
    ├── FullPort.slnx                  ← Visual Studio solution
    ├── Program.cs                     ← Application entry point & middleware config
    ├── appsettings.json               ← Configuration (E*TRADE credentials)
    ├── appsettings.Development.json   ← Development-specific config overrides
    │
    ├── Controllers/
    │   ├── PortfolioController.cs     ← Authentication, accounts, positions, balances
    │   ├── RebalanceController.cs     ← Rebalancing models, calculation, execution
    │   ├── MarketController.cs        ← Quotes, market indices, ETF suggestions
    │   ├── AnalyticsController.cs     ← Portfolio health, correlation, benchmarks
    │   └── WeatherForecastController.cs ← .NET template sample (unused)
    │
    ├── Models/
    │   └── RebalanceModels.cs         ← All DTOs, enums, and static mappings
    │
    ├── Properties/
    │   └── launchSettings.json        ← Development server profiles (ports)
    │
    └── wwwroot/
        └── index.html                 ← Complete SPA frontend (3,378 lines)
```

### File Size & Complexity

| File | Lines | Responsibility |
|---|---|---|
| `PortfolioController.cs` | ~1,440 | Auth, accounts, positions, balances, OAuth helper |
| `RebalanceController.cs` | ~1,007 | 9 strategies, trade calculation, order execution |
| `AnalyticsController.cs` | ~832 | Health scoring, correlation matrix, benchmarks |
| `RebalanceModels.cs` | ~765 | All models, enums, 150+ ETF mappings |
| `MarketController.cs` | ~452 | Quotes, indices, ETF suggestions, portfolio builder |
| `index.html` | ~3,378 | Entire frontend: HTML, CSS, and 60+ JS functions |
| `Program.cs` | 42 | Entry point and middleware setup |

---

## 5. Backend Deep Dive

### 5.1 Application Entry Point (`Program.cs`)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("/index.html");

app.Run();
```

**Key configuration choices**:
- **Enum serialization**: Enums serialize as strings (e.g., `"USStock"` not `0`)
- **Case insensitivity**: Frontend can send `camelCase`, backend accepts either
- **camelCase output**: All JSON output uses JavaScript naming conventions
- **SPA fallback**: Any route that doesn't match a controller or static file serves `index.html`

### 5.2 Middleware Pipeline

The request pipeline processes in this order:

```
Request
  → HTTPS Redirection (redirect HTTP → HTTPS)
  → Authorization (no auth middleware configured, but hook exists)
  → Default Files (serve index.html for /)
  → Static Files (serve wwwroot/ contents)
  → Controller Routing (api/* endpoints)
  → SPA Fallback (anything else → index.html)
```

### 5.3 Controllers

Each controller follows the same pattern:

1. **Route prefix**: `[Route("api/[controller]")]` → `/api/portfolio`, `/api/rebalance`, etc.
2. **Configuration injection**: `IConfiguration` for E\*TRADE credentials
3. **Logger injection**: `ILogger<T>` for structured logging
4. **Token access**: Static token properties from `PortfolioController`
5. **OAuth signing**: Private `BuildOAuthHeader()` method for E\*TRADE requests
6. **API base URL**: Dynamic property switching between production and sandbox

#### Controller Dependency Map

```
PortfolioController
  ├── Owns: _accessToken, _accessTokenSecret (static)
  ├── Owns: _requestTokens (ConcurrentDictionary)
  ├── Exposes: GetAccessToken(), GetAccessTokenSecret() (static)
  └── Contains: BuildOAuthHeader() (private)

RebalanceController
  ├── Reads: PortfolioController.GetAccessToken()
  ├── Reads: PortfolioController.GetAccessTokenSecret()
  ├── Uses: RebalanceModels (models, enums, mappings)
  └── Contains: BuildOAuthHeader() (private, duplicate)

MarketController
  ├── Reads: PortfolioController.GetAccessToken()
  ├── Reads: PortfolioController.GetAccessTokenSecret()
  ├── Uses: RebalanceModels (QuoteData, SuggestedETF, etc.)
  └── Contains: BuildOAuthHeader() (private, duplicate)

AnalyticsController
  ├── Reads: PortfolioController.GetAccessToken()
  ├── Reads: PortfolioController.GetAccessTokenSecret()
  ├── Contains: OAuthHelper (static class, centralized)
  └── Uses: RebalanceModels (AssetClassMappings)
```

### 5.4 Models & Data Structures

All models are defined in `Models/RebalanceModels.cs`.

#### Enums

```csharp
// Risk levels from lowest to highest
enum RiskProfile {
    UltraConservative, Conservative, Income, Balanced, Moderate,
    DividendGrowth, Growth, Aggressive, UltraAggressive, Custom
}

// Asset categories for classification
enum AssetClass {
    USStock, InternationalStock, Bond, Cash, Commodity, RealEstate, Crypto, Other
}
```

#### Core Models

| Model | Purpose | Key Fields |
|---|---|---|
| `TargetAllocation` | Represents desired vs. current allocation | `Symbol`, `AssetClass`, `TargetPercent`, `CurrentPercent`, `DifferenceValue` |
| `RebalanceModel` | A rebalancing strategy definition | `Name`, `Description`, `RiskProfile`, `Allocations[]`, `CashReservePercent` |
| `RebalanceTrade` | A computed trade order | `Symbol`, `Action` (BUY/SELL), `Quantity`, `EstimatedPrice`, `OrderType`, `PreviewId` |
| `RebalanceRequest` | Input for trade calculation | `AccountIdKey`, `Model`, `SellToRebalance`, `MinimumTradeValue`, `TolerancePercent` |
| `RebalanceResponse` | Output of trade calculation | `TotalPortfolioValue`, `CurrentAllocations[]`, `TargetAllocations[]`, `RequiredTrades[]`, `Warnings[]` |
| `ExecuteRebalanceRequest` | Input for trade execution | `Trades[]`, `PreviewOnly` (default: true) |
| `TradeExecutionResult` | Result per trade | `Symbol`, `Action`, `Success`, `OrderId`, `PreviewId`, `Status` |
| `QuoteData` | Market quote data | `Symbol`, `LastPrice`, `Change`, `ChangePercent`, `Bid`, `Ask`, `Volume` |
| `SuggestedETF` | Recommended ETF | `Symbol`, `Name`, `Description`, `CurrentPrice`, `ChangePercent` |
| `BuildPortfolioRequest` | Portfolio builder input | `CashToInvest`, `RiskProfile`, `CashReservePercent`, `Items[]` |
| `PortfolioBuildItem` | Single builder item | `Symbol`, `AssetClass`, `TargetPercent`, `FixedShares` |

#### Static Mappings

**`AssetClassMappings.CommonETFs`** — 150+ ticker-to-asset-class mappings:

| Category | Example Tickers | Asset Class |
|---|---|---|
| US Total Market | VTI, ITOT, SPTM | `USStock` |
| US Large Cap | VOO, SPY, IVV | `USStock` |
| US Growth | VUG, QQQ, QQQM, VGT | `USStock` |
| US Value | VTV, SCHV, RPV | `USStock` |
| US Dividend | SCHD, VYM, DVY, DGRO, NOBL, VIG | `USStock` |
| US Small/Mid Cap | VB, IJR, VO, IJH, IWM | `USStock` |
| US Factor | MTUM, QUAL, USMV, VLUE, RSP | `USStock` |
| Int'l Total | VXUS, IXUS | `InternationalStock` |
| Int'l Developed | VEA, IEFA, EFA, SCHF | `InternationalStock` |
| Int'l Emerging | VWO, IEMG, EEM | `InternationalStock` |
| Int'l Regional | VGK, EWJ, VPL, MCHI | `InternationalStock` |
| Bonds Aggregate | BND, AGG, SCHZ | `Bond` |
| Bonds Treasury | GOVT, SHY, IEF, TLT | `Bond` |
| Bonds TIPS | TIP, VTIP, SCHP | `Bond` |
| Bonds Corporate | VCIT, LQD, VCSH | `Bond` |
| Bonds High Yield | HYG, JNK, SHYG | `Bond` |
| Bonds Municipal | MUB, VTEB, TFI | `Bond` |
| Gold | GLD, IAU, GLDM | `Commodity` |
| Silver | SLV, SIVR | `Commodity` |
| Broad Commodities | DJP, GSG, DBC | `Commodity` |
| Real Estate | VNQ, SCHH, IYR, XLRE | `RealEstate` |
| Crypto ETFs | IBIT, BITO, GBTC, ETHE | `Crypto` |

**`AssetClassMappings.SuggestedETFs`** — A dictionary keyed by `AssetClass` containing lists of `SuggestedETF` objects with names and descriptions for each ETF. Used by the Portfolio Builder and Rebalancing UI.

### 5.5 OAuth 1.0a Implementation

FullPort implements OAuth 1.0a from scratch using `System.Security.Cryptography.HMACSHA1`. The `BuildOAuthHeader` method constructs signed authorization headers for every E\*TRADE API request.

**Signature process**:

```
1. Collect OAuth parameters:
   - oauth_consumer_key
   - oauth_nonce (GUID)
   - oauth_signature_method = "HMAC-SHA1"
   - oauth_timestamp (Unix epoch seconds)
   - oauth_token (access token)
   - oauth_version = "1.0"

2. Merge with any query parameters

3. Build signature base string:
   "{METHOD}&{percent-encoded URL}&{percent-encoded sorted params}"

4. Build signing key:
   "{percent-encoded consumer secret}&{percent-encoded token secret}"

5. Compute HMAC-SHA1(signing key, base string) → Base64

6. Build Authorization header:
   "OAuth oauth_consumer_key="...", ..., oauth_signature="...""
```

> **Implementation note**: The `BuildOAuthHeader` method is currently duplicated across PortfolioController, RebalanceController, and MarketController. AnalyticsController has a centralized `OAuthHelper` static class.

### 5.6 Shared Authentication State

Authentication tokens are stored as **static fields** in `PortfolioController`:

```csharp
private static readonly ConcurrentDictionary<string, string> _requestTokens = new();
private static string? _accessToken;
private static string? _accessTokenSecret;

public static string? GetAccessToken() => _accessToken;
public static string? GetAccessTokenSecret() => _accessTokenSecret;
```

Other controllers access tokens via these static methods:

```csharp
private static string? AccessToken => PortfolioController.GetAccessToken();
private static string? AccessTokenSecret => PortfolioController.GetAccessTokenSecret();
```

**Implications**:
- Single-user only — one set of tokens for the whole server
- Tokens are lost on application restart
- No session management or token refresh
- Thread-safe for reads (simple reference reads) but writes to `_accessToken` are not interlocked

---

## 6. Frontend Deep Dive

### 6.1 SPA Architecture

The entire frontend lives in a single file: `wwwroot/index.html` (~3,378 lines).

```
index.html
├── <head>
│   ├── Meta tags (charset, viewport)
│   ├── Bootstrap 5.3.0 CSS (CDN)
│   ├── Bootstrap Icons 1.11.0 (CDN)
│   └── <style> — Custom CSS (~200 lines)
│
├── <body>
│   ├── Navigation tabs (Dashboard, Holdings, Rebalance, Analytics, Settings)
│   ├── Tab content panels (one <div> per tab, toggled via Bootstrap tabs)
│   ├── Auth modal overlay
│   └── Bootstrap 5.3.0 JS bundle (CDN)
│
└── <script>
    └── All application JavaScript (~2,500 lines, 60+ functions)
```

### 6.2 Tab System

The UI is organized into **5 main tabs** plus sub-modes:

| Tab | Purpose | Key API Calls |
|---|---|---|
| **Dashboard** | Portfolio overview, market indices, accounts | `GET /api/portfolio`, `GET /api/market/indices` |
| **Holdings** | Position list with sorting, filtering, details | `GET /api/portfolio/holdings-detailed` |
| **Rebalance** | Strategy selection, trade calculation/execution | `GET /api/rebalance/models`, `POST /api/rebalance/calculate`, `POST /api/rebalance/execute` |
| **Analytics** | Health score, correlation, benchmarks | `GET /api/analytics/portfolio-health`, `GET /api/analytics/correlation`, `GET /api/analytics/benchmark-comparison` |
| **Settings** | Preferences, user name, dev tools | localStorage only (no API calls) |

The **Rebalance tab** has two sub-modes:
- **Rebalance Existing** — Rebalance current portfolio toward a target
- **Build New Positions** — Invest new cash using a strategy

### 6.3 JavaScript Function Reference

All functions are defined in a `<script>` block at the bottom of `index.html`. Here is a complete reference grouped by domain:

#### Authentication & Setup

| Function | Description |
|---|---|
| `openETradeAuth()` | Opens E\*TRADE OAuth popup, polls for PIN entry |
| `fetchAuthInfo()` | Calls `GET /api/portfolio/authenticate` to start OAuth flow |

#### Dashboard

| Function | Description |
|---|---|
| `loadPortfolio()` | Fetches portfolio data, renders dashboard cards |
| `fetchMarketIndices()` | Fetches market index prices (SPY, QQQ, DIA, IWM) |
| `renderMarketIndices(data)` | Renders index cards with price and change |
| `renderMarketStatusBadge()` | Shows market open/closed/pre-market badge |
| `updateMarketStatusHeader()` | Updates header with market status |
| `getMarketStatus()` | Calculates if market is open based on time |
| `getPersonalizedGreeting(userName)` | Returns "Good morning/afternoon/evening, {name}" |

#### Holdings

| Function | Description |
|---|---|
| `loadHoldings(chartOnly)` | Fetches detailed holdings, optionally chart-only |
| `renderDetailedHoldings(data)` | Renders the full holdings view with summary cards |
| `renderHoldingsTable(holdings)` | Renders holdings in table format |
| `renderHoldingsCards(holdings)` | Renders holdings in card format |
| `renderHoldingDetails(h)` | Renders expanded detail view for one holding |
| `toggleHoldingDetails(symbol)` | Toggles detail expansion for a holding |
| `setViewMode(mode)` | Switches between table and card views |
| `sortHoldings(column)` | Sorts holdings by the selected column |
| `filterHoldings(query)` | Filters holdings by search text |
| `filterByAssetClass(value)` | Filters holdings by asset class dropdown |
| `filterByGainLoss(value)` | Filters by gain/loss status |
| `filterBySearch(value)` | Applies search filter |
| `renderHoldings(data)` | Main holdings render dispatcher |
| `formatLargeNumber(num)` | Formats numbers as $1.2M, $500K, etc. |

#### Rebalancing

| Function | Description |
|---|---|
| `loadRebalance()` | Fetches rebalancing models and current analysis |
| `renderRebalance()` | Renders the rebalance UI with strategy cards |
| `switchRebalanceMode(mode)` | Switches between "rebalance" and "build" modes |
| `selectModel(idx)` | Selects a pre-built rebalancing strategy |
| `calculateRebalance()` | Calls `POST /api/rebalance/calculate` |
| `renderTradePreview()` | Shows computed trades in a preview table |
| `previewTrades()` | Calls `POST /api/rebalance/execute` with `previewOnly: true` |
| `executeTrades()` | Calls `POST /api/rebalance/execute` with `previewOnly: false` |
| `setTestCash(amount)` | Sets simulated cash for dev testing |
| `clearTestCash()` | Clears simulated cash |

#### Portfolio Builder

| Function | Description |
|---|---|
| `renderPortfolioBuilder(analysis)` | Renders the portfolio builder interface |
| `loadSuggestedETFs()` | Fetches suggested ETFs with prices |
| `selectBuilderModel(idx)` | Selects a strategy for building |
| `updateStrategyCards()` | Updates strategy card UI state |
| `getQuote()` | Fetches a quote for a specific symbol |
| `getTotalAllocationPercent()` | Calculates total allocation percentage |
| `toggleAddByType()` | Toggles add-by-dollars vs. add-by-shares |
| `addCustomStock()` | Adds a custom stock/ETF to the build list |
| `removeFromBuild(idx)` | Removes an item from the build list |
| `renderETFSelection()` | Renders the ETF selection UI per asset class |
| `changeETF(idx, symbol)` | Changes which ETF is selected for an asset class |
| `buildPortfolio()` | Calls `POST /api/market/build-portfolio` |
| `renderBuildPreview()` | Shows the build preview |
| `previewBuildTrades()` | Previews build trades |
| `executeBuildTrades()` | Executes build trades |

#### Saved Portfolios

| Function | Description |
|---|---|
| `getSavedPortfolios()` | Retrieves saved portfolios from localStorage |
| `savePortfolio(name)` | Saves current portfolio configuration |
| `loadSavedPortfolio(portfolioId)` | Loads a saved portfolio |
| `deleteSavedPortfolio(portfolioId)` | Deletes a saved portfolio |
| `showSavePortfolioModal()` | Shows the save portfolio dialog |
| `loadAndRenderPortfolio(portfolioId)` | Loads and renders a saved portfolio |
| `deleteAndRefresh(portfolioId)` | Deletes and refreshes the portfolio list |
| `clearSavedPortfolios()` | Clears all saved portfolios |

#### Analytics

| Function | Description |
|---|---|
| `loadAnalytics()` | Fetches all analytics data (health, correlation, benchmarks) |
| `renderAnalytics()` | Renders the analytics dashboard |

#### Settings

| Function | Description |
|---|---|
| `getSettings()` | Retrieves settings from localStorage |
| `saveSettings(settings)` | Saves settings to localStorage |
| `getUserName()` | Gets saved user name |
| `setUserName(name)` | Saves user name |
| `renderSettings()` | Renders the settings panel |
| `saveAllSettings()` | Saves all settings at once |
| `resetAllSettings()` | Resets to defaults |
| `promptUserName()` | Prompts user for their name |

### 6.4 Client-Side State Management

The frontend uses a combination of **module-level variables** and **localStorage** for state:

#### In-Memory State (JavaScript variables)

```javascript
// Current data
let portfolioData = null;      // Last loaded portfolio response
let holdingsData = null;        // Last loaded holdings response
let rebalanceModels = [];       // Available rebalancing strategies
let rebalanceAnalysis = null;   // Current portfolio analysis
let currentTrades = [];         // Computed trades pending execution
let suggestedETFs = {};         // ETFs by asset class
let buildItems = [];            // Portfolio builder items

// UI state
let selectedModelIndex = -1;    // Selected rebalancing strategy
let holdingsSortColumn = 'value';
let holdingsSortDirection = 'desc';
let holdingsViewMode = 'table';
let testCash = null;            // Dev mode simulated cash
```

#### localStorage Keys

| Key | Content | Default |
|---|---|---|
| `fullport_settings` | JSON object with `minTradeValue`, `tolerancePercent`, display prefs | `{ minTradeValue: 50, tolerancePercent: 2 }` |
| `fullport_username` | User's display name | `null` |
| `fullport_portfolios` | JSON array of saved portfolio configurations | `[]` |

### 6.5 Styling & Theming

Custom CSS is defined inline in `<style>` tags within `index.html`:

| CSS Class | Purpose |
|---|---|
| `.summary-card` | Purple gradient card for portfolio totals |
| `.stat-card` | White card with shadow for individual stats |
| `.chart-card` | White card for chart sections |
| `.account-card` | White card for account summaries (hover lift effect) |
| `.model-card` | Selectable strategy card (purple border on select) |
| `.rebalance-card` | Container for rebalancing sections |
| `.trade-buy` | Green-tinted row for buy trades |
| `.trade-sell` | Red-tinted row for sell trades |
| `.allocation-bar` | Horizontal bar for allocation visualization |
| `.gain-positive` | Green text for gains |
| `.gain-negative` | Red text for losses |

**Color scheme**:
- Primary gradient: `linear-gradient(135deg, #667eea 0%, #764ba2 100%)` (purple)
- Positive/gains: `#198754` (Bootstrap green)
- Negative/losses: `#dc3545` (Bootstrap red)
- Card shadows: `0 2px 8px rgba(0,0,0,0.08)`

---

## 7. Complete API Reference

All endpoints return JSON. All authenticated endpoints require a valid OAuth session (access token stored server-side).

### 7.1 Portfolio Controller

**Base URL**: `/api/portfolio`

---

#### `GET /api/portfolio`

Get all accounts with balances.

**Authentication**: Required

**Response** (200):
```json
{
  "accounts": [
    {
      "accountIdKey": "abc123",
      "accountId": "12345678",
      "accountDesc": "Individual Brokerage",
      "accountType": "INDIVIDUAL",
      "accountMode": "CASH",
      "accountStatus": "ACTIVE",
      "balance": {
        "cash": 5432.10,
        "securities": 94567.90,
        "totalValue": 100000.00
      }
    }
  ],
  "summary": {
    "totalAccounts": 2,
    "totalCash": 10864.20,
    "totalSecurities": 189135.80,
    "totalPortfolioValue": 200000.00
  }
}
```

**Response** (401):
```json
{
  "message": "Not authenticated",
  "authUrl": "/api/portfolio/authenticate"
}
```

---

#### `GET /api/portfolio/authenticate`

Start OAuth 1.0a flow. Returns the E\*TRADE authorization URL.

**Authentication**: Not required

**Response** (200):
```json
{
  "authorizeUrl": "https://us.etrade.com/e/t/etws/authorize?key=...&token=...",
  "oauthToken": "request_token_value"
}
```

---

#### `POST /api/portfolio/access-token`

Exchange OAuth PIN for access token.

**Authentication**: Not required

**Request body**:
```json
{
  "oAuthToken": "request_token_value",
  "pin": "ABC123"
}
```

**Response** (200):
```json
{
  "success": true
}
```

---

#### `POST /api/portfolio/sign-out`

Clear all authentication tokens.

**Response** (200):
```json
{
  "success": true
}
```

---

#### `GET /api/portfolio/{accountIdKey}/positions`

Get positions for a specific account.

**Authentication**: Required

**Response** (200):
```json
{
  "accountIdKey": "abc123",
  "positions": [
    {
      "symbol": "VTI",
      "description": "Vanguard Total Stock Market ETF",
      "quantity": 100,
      "lastPrice": 250.00,
      "marketValue": 25000.00,
      "costBasis": 22000.00,
      "dayGain": 150.00,
      "totalGain": 3000.00,
      "totalGainPct": 13.64,
      "assetClass": "USStock"
    }
  ]
}
```

---

#### `GET /api/portfolio/all-positions`

Get aggregated positions across all accounts.

**Authentication**: Required

**Response** (200):
```json
{
  "positions": [ ... ],
  "summary": {
    "totalSymbols": 15,
    "totalMarketValue": 200000.00,
    "totalDayGain": 450.00,
    "totalGain": 35000.00,
    "totalCostBasis": 165000.00
  },
  "holdingsBySymbol": { ... }
}
```

---

#### `GET /api/portfolio/holdings-detailed`

Get comprehensive holdings with full metrics.

**Authentication**: Required

**Response** (200):
```json
{
  "holdings": [
    {
      "symbol": "VTI",
      "description": "Vanguard Total Stock Market ETF",
      "securityType": "ETF",
      "assetClass": "USStock",
      "quantity": 100,
      "marketValue": 25000.00,
      "costBasis": 22000.00,
      "averageCostPerShare": 220.00,
      "dayGain": 150.00,
      "dayGainPct": 0.60,
      "totalGain": 3000.00,
      "totalGainPct": 13.64,
      "portfolioWeight": 12.50,
      "lastPrice": 250.00,
      "bid": 249.95,
      "ask": 250.05,
      "spread": 0.10,
      "high52Week": 275.00,
      "low52Week": 210.00,
      "volume": 3500000,
      "averageVolume": 4000000,
      "volumeVsAverage": -12.50,
      "peRatio": 22.5,
      "dividendYield": 1.35,
      "beta": 1.00,
      "marketCap": 350000000000
    }
  ],
  "summary": {
    "totalPositions": 15,
    "totalMarketValue": 200000.00,
    "totalCostBasis": 165000.00,
    "totalGain": 35000.00,
    "totalGainPct": 21.21,
    "winningPositions": 12,
    "losingPositions": 3,
    "winRate": 80.00,
    "bestPerformer": { "symbol": "QQQ", "gainPct": 45.2 },
    "worstPerformer": { "symbol": "BNDX", "gainPct": -2.1 },
    "largestPosition": { "symbol": "VTI", "weight": 12.50 }
  },
  "diversification": {
    "assetClasses": {
      "USStock": { "value": 120000, "percent": 60 },
      "InternationalStock": { "value": 40000, "percent": 20 },
      "Bond": { "value": 30000, "percent": 15 },
      "RealEstate": { "value": 10000, "percent": 5 }
    },
    "concentrationTop5": 65.00,
    "annualDividendIncome": 3200.00,
    "portfolioYield": 1.60
  }
}
```

---

#### `GET /api/portfolio/debug-balance/{accountIdKey}`

Debug endpoint — returns raw E\*TRADE balance response.

**Authentication**: Required

---

### 7.2 Rebalance Controller

**Base URL**: `/api/rebalance`

---

#### `GET /api/rebalance/models`

Get all 9 pre-built rebalancing strategies.

**Authentication**: Not required

**Response** (200):
```json
[
  {
    "name": "Ultra-Conservative",
    "description": "Capital preservation: 10% stocks, 60% bonds, 20% cash",
    "riskProfile": "UltraConservative",
    "rebalanceByAssetClass": true,
    "cashReservePercent": 5,
    "allocations": [
      { "assetClass": "USStock", "targetPercent": 5 },
      { "assetClass": "InternationalStock", "targetPercent": 5 },
      { "assetClass": "Bond", "targetPercent": 60 },
      { "assetClass": "RealEstate", "targetPercent": 5 },
      { "assetClass": "Commodity", "targetPercent": 5 },
      { "assetClass": "Cash", "targetPercent": 20 }
    ]
  },
  ...
]
```

---

#### `GET /api/rebalance/analyze`

Analyze current portfolio allocation vs. any target.

**Authentication**: Required

**Query Parameters**:

| Parameter | Type | Description |
|---|---|---|
| `accountIdKey` | string | Optional — specific account (omit for all) |
| `testCash` | decimal | Optional — override cash balance (dev mode) |

**Response** (200):
```json
{
  "totalValue": 200000.00,
  "cashBalance": 10000.00,
  "currentAllocations": [
    { "assetClass": "USStock", "currentPercent": 55.0, "currentValue": 110000 },
    { "assetClass": "Bond", "currentPercent": 20.0, "currentValue": 40000 }
  ],
  "holdings": [ ... ]
}
```

---

#### `POST /api/rebalance/calculate`

Calculate the trades needed to rebalance.

**Authentication**: Required

**Request body**:
```json
{
  "accountIdKey": null,
  "model": {
    "name": "Balanced",
    "riskProfile": "Balanced",
    "rebalanceByAssetClass": true,
    "allocations": [
      { "assetClass": "USStock", "targetPercent": 40 },
      { "assetClass": "InternationalStock", "targetPercent": 20 },
      { "assetClass": "Bond", "targetPercent": 30 },
      { "assetClass": "RealEstate", "targetPercent": 5 },
      { "assetClass": "Commodity", "targetPercent": 5 }
    ],
    "cashReservePercent": 5
  },
  "sellToRebalance": true,
  "minimumTradeValue": 50,
  "tolerancePercent": 2
}
```

**Response** (200):
```json
{
  "totalPortfolioValue": 200000.00,
  "currentAllocations": [ ... ],
  "targetAllocations": [ ... ],
  "requiredTrades": [
    {
      "symbol": "BND",
      "description": "Vanguard Total Bond Market ETF",
      "action": "BUY",
      "quantity": 50,
      "estimatedPrice": 72.50,
      "estimatedValue": 3625.00,
      "orderType": "MARKET",
      "accountIdKey": "abc123"
    },
    {
      "symbol": "VTI",
      "description": "Vanguard Total Stock Market ETF",
      "action": "SELL",
      "quantity": 15,
      "estimatedPrice": 250.00,
      "estimatedValue": 3750.00,
      "orderType": "MARKET",
      "accountIdKey": "abc123"
    }
  ],
  "estimatedTotalBuys": 7250.00,
  "estimatedTotalSells": 7500.00,
  "tradeCount": 4,
  "warnings": ["Sell for VTI capped at 50% of position"]
}
```

---

#### `POST /api/rebalance/execute`

Preview or execute trades.

**Authentication**: Required

**Request body**:
```json
{
  "trades": [
    {
      "symbol": "BND",
      "action": "BUY",
      "quantity": 50,
      "estimatedPrice": 72.50,
      "orderType": "MARKET",
      "accountIdKey": "abc123"
    }
  ],
  "previewOnly": true
}
```

**Response** (200):
```json
{
  "previewOnly": true,
  "results": [
    {
      "symbol": "BND",
      "action": "BUY",
      "quantity": 50,
      "success": true,
      "previewId": "12345",
      "status": "PREVIEW"
    }
  ],
  "successCount": 1,
  "failedCount": 0
}
```

---

### 7.3 Market Controller

**Base URL**: `/api/market`

---

#### `GET /api/market/quote/{symbols}`

Get real-time quotes for one or more symbols (comma-separated).

**Authentication**: Required

**Example**: `GET /api/market/quote/VTI,BND,GLD`

**Response** (200):
```json
{
  "quotes": [
    {
      "symbol": "VTI",
      "description": "Vanguard Total Stock Market ETF",
      "lastPrice": 250.00,
      "change": 1.50,
      "changePercent": 0.60,
      "bid": 249.95,
      "ask": 250.05,
      "volume": 3500000,
      "high": 251.00,
      "low": 248.50,
      "open": 249.00,
      "previousClose": 248.50
    }
  ]
}
```

> **Note**: E\*TRADE limits to 25 symbols per request. The controller automatically batches larger requests.

---

#### `GET /api/market/indices`

Get major market index ETF prices.

**Authentication**: Optional (returns placeholder data if not authenticated)

**Response** (200):
```json
{
  "indices": [
    { "symbol": "SPY", "name": "S&P 500", "price": 520.00, "change": 3.50, "changePercent": 0.68, "isUp": true },
    { "symbol": "QQQ", "name": "Nasdaq 100", "price": 445.00, "change": -2.10, "changePercent": -0.47, "isUp": false },
    { "symbol": "DIA", "name": "Dow Jones", "price": 395.00, "change": 1.20, "changePercent": 0.30, "isUp": true },
    { "symbol": "IWM", "name": "Russell 2000", "price": 205.00, "change": 0.80, "changePercent": 0.39, "isUp": true }
  ],
  "timestamp": "2025-01-15T14:30:00Z"
}
```

---

#### `GET /api/market/suggested-etfs`

Get recommended ETFs by asset class with live prices.

**Authentication**: Required

**Response** (200):
```json
{
  "USStock": [
    { "symbol": "VTI", "name": "Vanguard Total Stock Market ETF", "description": "Total US market exposure", "currentPrice": 250.00, "changePercent": 0.60 },
    { "symbol": "VOO", "name": "Vanguard S&P 500 ETF", "description": "S&P 500 index", "currentPrice": 520.00, "changePercent": 0.68 }
  ],
  "InternationalStock": [ ... ],
  "Bond": [ ... ],
  "Commodity": [ ... ],
  "RealEstate": [ ... ],
  "Crypto": [ ... ]
}
```

---

#### `POST /api/market/build-portfolio`

Calculate shares to buy for a new portfolio.

**Authentication**: Required

**Request body**:
```json
{
  "accountIdKey": "abc123",
  "cashToInvest": 10000.00,
  "riskProfile": "Balanced",
  "cashReservePercent": 5,
  "items": [
    { "symbol": "VTI", "assetClass": "USStock", "targetPercent": 40 },
    { "symbol": "VXUS", "assetClass": "InternationalStock", "targetPercent": 20 },
    { "symbol": "BND", "assetClass": "Bond", "targetPercent": 30 },
    { "symbol": "VNQ", "assetClass": "RealEstate", "targetPercent": 10 }
  ]
}
```

**Response** (200):
```json
{
  "trades": [
    { "symbol": "VTI", "action": "BUY", "quantity": 15, "estimatedPrice": 250.00, "estimatedValue": 3750.00 },
    { "symbol": "VXUS", "action": "BUY", "quantity": 33, "estimatedPrice": 57.00, "estimatedValue": 1881.00 },
    { "symbol": "BND", "action": "BUY", "quantity": 39, "estimatedPrice": 72.50, "estimatedValue": 2827.50 },
    { "symbol": "VNQ", "action": "BUY", "quantity": 10, "estimatedPrice": 90.00, "estimatedValue": 900.00 }
  ],
  "summary": {
    "totalCash": 10000.00,
    "cashReserve": 500.00,
    "totalInvestment": 9358.50,
    "remainingCash": 641.50,
    "tradeCount": 4
  }
}
```

---

### 7.4 Analytics Controller

**Base URL**: `/api/analytics`

---

#### `GET /api/analytics/portfolio-health`

Get comprehensive portfolio health analysis.

**Authentication**: Required

**Response** (200):
```json
{
  "hasData": true,
  "totalValue": 200000.00,
  "healthScore": 78,
  "concentrationAnalysis": {
    "herfindahlIndex": 0.085,
    "topHoldingPercent": 12.5,
    "top5Percent": 45.0,
    "numberOfPositions": 15,
    "effectiveDiversification": "Good"
  },
  "sectorExposure": {
    "USStock": { "percent": 55.0, "status": "Overweight" },
    "Bond": { "percent": 20.0, "status": "Underweight" },
    "InternationalStock": { "percent": 15.0, "status": "Slightly Underweight" }
  },
  "benchmarkData": [
    { "symbol": "SPY", "name": "S&P 500", "dayChangePercent": 0.68 }
  ],
  "recommendations": [
    "Consider increasing bond allocation for balance",
    "International exposure is below typical targets"
  ]
}
```

---

#### `GET /api/analytics/correlation`

Get correlation matrix between top holdings.

**Authentication**: Required

**Response** (200):
```json
{
  "hasData": true,
  "method": "historical",
  "dataPoints": 252,
  "symbols": ["VTI", "VXUS", "BND", "VNQ", "GLD", "QQQ"],
  "matrix": [
    [1.00, 0.82, -0.15, 0.65, 0.08, 0.95],
    [0.82, 1.00, -0.10, 0.55, 0.12, 0.78],
    [-0.15, -0.10, 1.00, 0.20, 0.30, -0.18],
    [0.65, 0.55, 0.20, 1.00, 0.15, 0.60],
    [0.08, 0.12, 0.30, 0.15, 1.00, 0.05],
    [0.95, 0.78, -0.18, 0.60, 0.05, 1.00]
  ],
  "note": "Pearson correlation based on 252 days of daily returns.",
  "interpretation": {
    "highPositive": "> 0.7: Strong positive correlation",
    "lowPositive": "0.3 to 0.7: Moderate positive correlation",
    "nearZero": "-0.3 to 0.3: Low/no correlation - good for diversification",
    "lowNegative": "-0.7 to -0.3: Moderate negative correlation",
    "highNegative": "< -0.7: Strong negative correlation (rare)"
  }
}
```

---

#### `GET /api/analytics/benchmark-comparison`

Compare portfolio performance against benchmarks.

**Authentication**: Required

**Response** (200):
```json
{
  "hasData": true,
  "portfolio": {
    "totalValue": 200000.00,
    "dayChange": 450.00,
    "dayChangePercent": 0.23
  },
  "benchmarks": [
    {
      "symbol": "SPY",
      "name": "S&P 500",
      "price": 520.00,
      "dayChangePercent": 0.68,
      "relativePerformance": -0.45,
      "beating": false
    },
    {
      "symbol": "AGG",
      "name": "US Aggregate Bond",
      "price": 100.50,
      "dayChangePercent": 0.05,
      "relativePerformance": 0.18,
      "beating": true
    }
  ],
  "summary": {
    "beatingCount": 1,
    "totalBenchmarks": 4,
    "bestRelative": "AGG",
    "worstRelative": "QQQ"
  }
}
```

---

## 8. Authentication Flow (Step by Step)

FullPort uses **OAuth 1.0a** with E\*TRADE's **Out-of-Band (OOB)** flow. This is the complete sequence:

### Step 1: User Clicks "Sign in with E\*TRADE"

The frontend calls `openETradeAuth()`, which:
1. Calls `fetchAuthInfo()` → `GET /api/portfolio/authenticate`

### Step 2: Backend Obtains Request Token

`PortfolioController.Authenticate()`:
1. Builds OAuth parameters (consumer key, nonce, timestamp, callback=`oob`)
2. Constructs the signature base string
3. Signs with HMAC-SHA1 using `{consumer_secret}&` (empty token secret)
4. POSTs to `{ApiBaseUrl}/oauth/request_token`
5. Parses response for `oauth_token` and `oauth_token_secret`
6. Stores request token secret in `_requestTokens[oauth_token]`
7. Returns `{ authorizeUrl, oauthToken }` to frontend

### Step 3: User Authorizes in Popup

The frontend opens a popup window to:
```
https://us.etrade.com/e/t/etws/authorize?key={consumerKey}&token={oauthToken}
```

The user:
1. Logs in to E\*TRADE
2. Reviews and authorizes the application
3. Receives a **verification PIN** on screen

### Step 4: User Enters PIN

The user copies the PIN and pastes it into the FullPort PIN input field.

### Step 5: Backend Exchanges PIN for Access Token

`PortfolioController.ExchangePin()`:
1. Retrieves request token secret from `_requestTokens`
2. Builds OAuth parameters (consumer key, request token, verifier PIN, nonce, timestamp)
3. Constructs signature base string
4. Signs with HMAC-SHA1 using `{consumer_secret}&{request_token_secret}`
5. POSTs to `{ApiBaseUrl}/oauth/access_token`
6. Parses response for `oauth_token` (access) and `oauth_token_secret` (access)
7. Stores tokens in static fields: `_accessToken`, `_accessTokenSecret`
8. Returns `{ success: true }` to frontend

### Step 6: Authenticated API Calls

All subsequent E\*TRADE API calls:
1. Use the stored access token and secret
2. Build a fresh OAuth header per request (new nonce and timestamp each time)
3. Sign the full request URL + parameters with HMAC-SHA1
4. Include the `Authorization: OAuth ...` header

### Authentication Diagram

```
  Browser                  FullPort Server              E*TRADE
    │                           │                          │
    │ 1. Click Sign In          │                          │
    ├──────────────────────────►│                          │
    │                           │ 2. POST /oauth/request_token
    │                           ├─────────────────────────►│
    │                           │ 3. oauth_token + secret  │
    │                           │◄─────────────────────────┤
    │ 4. { authorizeUrl }       │                          │
    │◄──────────────────────────┤                          │
    │                           │                          │
    │ 5. Open popup ────────────────────────────────────────►
    │                           │                          │
    │    User logs in & authorizes                         │
    │    Receives PIN           │                          │
    │◄──────────────────────────────────────────────────────│
    │                           │                          │
    │ 6. Submit PIN             │                          │
    ├──────────────────────────►│                          │
    │                           │ 7. POST /oauth/access_token
    │                           ├─────────────────────────►│
    │                           │ 8. access_token + secret │
    │                           │◄─────────────────────────┤
    │ 9. { success: true }      │                          │
    │◄──────────────────────────┤                          │
    │                           │                          │
    │ 10. Load portfolio        │                          │
    ├──────────────────────────►│ 11. Signed API calls     │
    │                           ├─────────────────────────►│
    │                           │ 12. Account data         │
    │                           │◄─────────────────────────┤
    │ 13. Dashboard data        │                          │
    │◄──────────────────────────┤                          │
```

---

## 9. Rebalancing Engine

### 9.1 Pre-Built Strategies

FullPort includes 9 pre-built rebalancing strategies, ordered from lowest to highest risk:

| # | Strategy | Risk Profile | Description |
|---|---|---|---|
| 1 | Ultra-Conservative | `UltraConservative` | Capital preservation — mostly bonds and cash |
| 2 | Conservative | `Conservative` | Low risk — heavy bonds, some equity |
| 3 | Income | `Income` | Yield-focused — dividends, bonds, REITs |
| 4 | Balanced | `Balanced` | Classic 60/40 all-weather approach |
| 5 | Moderate | `Moderate` | Balanced growth with diversification |
| 6 | Dividend Growth | `DividendGrowth` | Quality dividend growers + REITs |
| 7 | Growth | `Growth` | Capital appreciation — higher equity |
| 8 | Aggressive | `Aggressive` | High equity with crypto exposure |
| 9 | Ultra-Aggressive | `UltraAggressive` | Maximum growth — heavy crypto, emerging markets |

Plus **Custom** — user-defined allocation percentages.

### 9.2 Risk Profile Allocations

Exact allocation percentages per asset class for each risk profile:

| Asset Class | Ultra-Cons. | Cons. | Income | Balanced | Moderate | Div Growth | Growth | Aggressive | Ultra-Agg. |
|---|---|---|---|---|---|---|---|---|---|
| US Stock | 5% | 15% | 25% | 40% | 35% | 50% | 50% | 45% | 40% |
| Int'l Stock | 5% | 10% | 10% | 20% | 15% | 15% | 20% | 20% | 20% |
| Bond | 60% | 50% | 35% | 30% | 25% | 15% | 10% | 5% | — |
| Real Estate | 5% | 10% | 20% | 5% | 10% | 15% | 5% | 10% | 5% |
| Commodity | 5% | 5% | 5% | 5% | 5% | — | 5% | 5% | 10% |
| Crypto | — | — | — | — | 5% | — | 5% | 10% | 25% |
| Cash | 20% | 10% | 5% | — | 5% | 5% | 5% | 5% | — |

### 9.3 Trade Generation Logic

When calculating rebalance trades, the engine follows this algorithm:

1. **Fetch current holdings** — Get all positions with current market values
2. **Classify positions** — Map each symbol to an asset class using `AssetClassMappings.CommonETFs`
3. **Calculate current allocation** — Sum market values by asset class, compute percentages
4. **Compare to target** — For each asset class, compute: `difference = target% - current%`
5. **Apply tolerance** — Skip asset classes within the tolerance band (default ±2%)
6. **Generate sells** (if `sellToRebalance = true`):
   - Identify overweight positions
   - Cap any single sell at 50% of position value (protection)
   - Only sell enough to fund planned buys
7. **Generate buys**:
   - Identify underweight asset classes
   - Use available cash + sell proceeds
   - Select default ETFs for missing asset classes (VTI, VXUS, BND, VNQ, GLD, IBIT)
   - Calculate share quantities: `Math.Floor(targetValue / currentPrice)`
8. **Apply minimum trade filter** — Remove trades below minimum value (default $50)
9. **Generate warnings** — Capped sells, skipped trades, etc.

### 9.4 Asset Class Mappings

When the rebalancing engine encounters a symbol, it looks it up in the `AssetClassMappings.CommonETFs` dictionary:

```csharp
// Example lookups:
"VTI"  → AssetClass.USStock
"VXUS" → AssetClass.InternationalStock
"BND"  → AssetClass.Bond
"VNQ"  → AssetClass.RealEstate
"GLD"  → AssetClass.Commodity
"IBIT" → AssetClass.Crypto
```

For symbols not in the dictionary, the position is classified as `AssetClass.Other`.

**Default ETFs for missing asset classes** (used when buying into an asset class where the user has no existing positions):

| Asset Class | Default ETF | Full Name |
|---|---|---|
| US Stock | VTI | Vanguard Total Stock Market ETF |
| Int'l Stock | VXUS | Vanguard Total International Stock ETF |
| Bond | BND | Vanguard Total Bond Market ETF |
| Real Estate | VNQ | Vanguard Real Estate ETF |
| Commodity | GLD | SPDR Gold Shares |
| Crypto | IBIT | iShares Bitcoin Trust |

---

## 10. Portfolio Builder

The Portfolio Builder is a separate mode within the Rebalance tab that allows users to **invest new cash** rather than rebalance existing positions.

### How It Works

1. **User selects a strategy** (or builds custom allocation)
2. **ETFs are suggested** for each asset class (from `AssetClassMappings.SuggestedETFs`)
3. **User can customize** — swap ETFs, adjust percentages, add individual stocks
4. **User enters cash amount** to invest
5. **System calculates share quantities** for each position:
   ```
   investable_cash = total_cash × (1 - cash_reserve_percent / 100)
   target_value = investable_cash × (target_percent / 100)
   shares = Math.Floor(target_value / current_price)
   ```
6. **Preview shows estimated trades** with prices and values
7. **User can execute** — trades are sent to E\*TRADE

### Custom Features

- **Add by Dollar Amount**: Enter a dollar value, system calculates shares
- **Add by Share Count**: Enter exact number of shares
- **Save/Load Portfolios**: Save configurations to localStorage for reuse
- **Real-time Quotes**: Fetch current prices before building

---

## 11. Analytics Engine

### Portfolio Health Score (0–100)

The health score is a composite metric calculated from:

| Factor | Weight | What It Measures |
|---|---|---|
| Diversification | 30% | Number of positions, Herfindahl index |
| Concentration risk | 25% | Top holding %, top 5 holdings % |
| Asset class coverage | 25% | How many asset classes are represented |
| Balance | 20% | How close to equal-weight the portfolio is |

### Correlation Matrix

- Uses **Pearson correlation** on daily returns
- Limited to **top 8 holdings** by value for display clarity
- Falls back to **day-change proxy** if historical data is unavailable
- Values range from `-1` (perfectly inverse) to `+1` (perfectly correlated)

### Benchmark Comparison

Compares portfolio's daily performance against:
- **SPY** — S&P 500
- **QQQ** — Nasdaq 100
- **VTI** — Total US Market
- **AGG** — US Aggregate Bonds

Reports `relativePerformance` = portfolio change% − benchmark change%.

---

## 12. Configuration Reference

### `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ETrade": {
    "ConsumerKey": "YOUR_CONSUMER_KEY",
    "ConsumerSecret": "YOUR_CONSUMER_SECRET",
    "UseSandbox": false
  }
}
```

### Configuration Methods (by precedence)

| Priority | Method | Example |
|---|---|---|
| 1 (highest) | Command-line args | `--ETrade:ConsumerKey=abc` |
| 2 | Environment variables | `ETrade__ConsumerKey=abc` |
| 3 | User Secrets (dev) | `dotnet user-secrets set "ETrade:ConsumerKey" "abc"` |
| 4 | `appsettings.{Env}.json` | `appsettings.Development.json` |
| 5 (lowest) | `appsettings.json` | Direct file edit |

### Launch Profiles (`Properties/launchSettings.json`)

| Profile | URL | Purpose |
|---|---|---|
| `http` | `http://localhost:5104` | HTTP-only development |
| `https` | `https://localhost:7043` | HTTPS development (default) |

### Frontend Settings (localStorage)

| Setting | Default | Description |
|---|---|---|
| `minTradeValue` | 50 | Minimum dollar value per trade |
| `tolerancePercent` | 2 | Skip rebalancing within this % of target |
| `holdingsViewMode` | `"table"` | Default holdings display mode |
| `showGainLoss` | `true` | Show gain/loss columns |

---

## 13. Development Workflow

### Prerequisites

1. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
2. E\*TRADE Developer Account ([developer.etrade.com](https://developer.etrade.com))

### Getting Started

```bash
# Clone
git clone https://github.com/djEdwards/FullPort.git
cd FullPort/FullPort

# Configure credentials (recommended: user secrets)
dotnet user-secrets init
dotnet user-secrets set "ETrade:ConsumerKey" "YOUR_KEY"
dotnet user-secrets set "ETrade:ConsumerSecret" "YOUR_SECRET"

# Run
dotnet run

# Open browser
# https://localhost:7043  (or http://localhost:5104)
```

### Development Mode Features

When `ASPNETCORE_ENVIRONMENT=Development`:

- **OpenAPI docs** available at `/openapi/v1.json`
- **Test cash simulation** — Override cash balance without real money:
  - Use `setTestCash(10000)` in browser console
  - Quick cash buttons: $1K, $5K, $10K, $50K, $100K
  - `clearTestCash()` to reset
- **Verbose logging** — Full API request/response details in console
- **Sandbox mode** — Set `ETrade:UseSandbox = true` to use E\*TRADE's sandbox API

### E\*TRADE Sandbox vs. Production

| Feature | Sandbox | Production |
|---|---|---|
| Base URL | `https://apisb.etrade.com` | `https://api.etrade.com` |
| Real data | No (simulated) | Yes |
| Real trading | No | Yes |
| Rate limits | Relaxed | Standard |
| Config | `"UseSandbox": true` | `"UseSandbox": false` |

### Build & Run Commands

```bash
# Build
dotnet build

# Run (development)
dotnet run

# Run (production)
dotnet run --environment Production

# Publish for deployment
dotnet publish -c Release -o ./publish
```

---

## 14. Glossary

| Term | Definition |
|---|---|
| **Access Token** | OAuth token granting API access after user authorization |
| **Asset Class** | Category of investment: US Stock, Int'l Stock, Bond, Cash, Commodity, Real Estate, Crypto |
| **Benchmark** | Market index used for performance comparison (SPY, QQQ, VTI, AGG) |
| **Correlation Matrix** | Grid showing how assets move relative to each other (-1 to +1) |
| **Consumer Key** | App identifier issued by E\*TRADE for API access |
| **Consumer Secret** | App secret used for OAuth signature generation |
| **ETF** | Exchange-Traded Fund — a basket of securities trading like a stock |
| **Herfindahl Index** | Concentration measure — sum of squared portfolio weights |
| **HMAC-SHA1** | Hash-based Message Authentication Code using SHA-1 — used for OAuth signatures |
| **OAuth 1.0a** | Authentication protocol requiring signed requests |
| **OOB (Out-of-Band)** | OAuth callback method where user manually copies a PIN |
| **Position** | A holding of a specific security (e.g., 100 shares of VTI) |
| **Rebalancing** | Adjusting portfolio weights to match a target allocation |
| **Request Token** | Temporary OAuth token used during the authorization flow |
| **Risk Profile** | Predefined allocation strategy from Ultra-Conservative to Ultra-Aggressive |
| **SPA** | Single Page Application — one HTML file with dynamic JavaScript content |
| **Tolerance Band** | Percentage threshold — skip rebalancing if within this range of target |

---

*This guide reflects the codebase as of the current version. For user-facing documentation, see [README.md](README.md).*
