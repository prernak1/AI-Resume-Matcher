# 🤖 AI Resume Matcher

> Upload your resume PDF and instantly discover your best-fit jobs from **live company job boards** — no login, no signup, no fluff.

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-9.0-512BD4?style=flat-square&logo=dotnet)
![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)
![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen?style=flat-square)

---

## 📸 Demo

| Upload | Analysis & Matches |
|--------|--------------------|
| Drag & drop your PDF resume | Skills are extracted and matched against live jobs |

> Live job data is fetched directly from a company's **Greenhouse job board** (e.g. Stripe, Airbnb, GitHub). No hardcoded listings.

---

## ✨ Features

- **PDF text extraction** — powered by [PdfPig](https://github.com/UglyToad/PdfPig); no cloud OCR service required
- **Skill detection** — 60+ technologies and frameworks matched via regex (C#, Python, Docker, Kubernetes, AWS, and more)
- **Live job data** — fetches real published jobs from the [Greenhouse Job Board API](https://developers.greenhouse.io/job-board.html)
- **Smart matching** — weighted scoring (70 % required skills, 30 % preferred) with confidence scaling
- **Clean web UI** — drag & drop upload, animated match bars, skill chips, one-click job apply
- **No database required** — jobs are fetched on demand and cached in-memory for 10 minutes
- **Secrets-safe** — zero credentials in source control; uses .NET User Secrets locally

---

## 🏗️ Architecture

Clean Architecture — each layer has a single responsibility and dependencies flow inward only.

```
┌─────────────────────────────────────────────────────────┐
│  ResumeMatcher.Api          (HTTP / controllers / UI)   │
├─────────────────────────────────────────────────────────┤
│  ResumeMatcher.Application  (interfaces / use cases)    │
├─────────────────────────────────────────────────────────┤
│  ResumeMatcher.Domain       (entities — Job, Resume…)   │
├─────────────────────────────────────────────────────────┤
│  ResumeMatcher.Infrastructure                           │
│    ├── PdfPigTextExtractor   (PDF → plain text)         │
│    ├── KeywordResumeAnalyzer (text → skill list)        │
│    ├── SkillBasedJobMatcher  (skills × jobs → scores)   │
│    └── GreenhouseJobRepository (live jobs via REST)     │
└─────────────────────────────────────────────────────────┘
```

**Request flow for `POST /api/resume/match`:**

```
PDF upload → extract text → detect skills → fetch Greenhouse jobs → score & rank → return top N
```

---

## 🚀 Quick Start

### Prerequisites

| Tool | Version |
|------|---------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 9.0+ |
| A Greenhouse board token | e.g. `stripe`, `airbnb` |
| *(Optional)* OpenAI API key | For future AI-powered analysis |

### 1 — Clone

```bash
git clone https://github.com/prernak1/AI-Resume-Matcher.git
cd AI-Resume-Matcher
```

### 2 — Set secrets

Secrets are stored **outside the repo** using [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets). They never get committed to git.

```bash
cd src/ResumeMatcher.Api

# Required — the board token from your target company's Greenhouse URL
# e.g. https://boards.greenhouse.io/stripe → board token is "stripe"
dotnet user-secrets set "Greenhouse:BoardToken" "stripe"

# Optional — only needed if you enable OpenAI-powered analysis
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."

# Optional — only needed if you add a PostgreSQL database (Step 3 of the roadmap)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=resume_matcher;Username=postgres;Password=yourpassword"
```

### 3 — Run

```bash
dotnet run --project src/ResumeMatcher.Api --urls "http://localhost:5100"
```

Open **http://localhost:5100** in your browser, drop in a PDF resume, and see your matches.

---

## ⚙️ Configuration

All configuration keys and their defaults (in `appsettings.json`):

| Key | Default | Description |
|-----|---------|-------------|
| `Greenhouse:BoardToken` | *(empty)* | Company slug from their Greenhouse URL. Falls back to hardcoded sample jobs when not set. |
| `Greenhouse:CacheMinutes` | `10` | How long live job data is cached in-memory |
| `OpenAI:ApiKey` | *(empty)* | Optional — used by a future OpenAI-powered analyzer |
| `OpenAI:Model` | `gpt-4o-mini` | OpenAI model to use |
| `ConnectionStrings:DefaultConnection` | *(empty)* | PostgreSQL — only needed in Step 3 of the roadmap |

> **Finding a board token:** Go to any company's Greenhouse job board URL, e.g. `https://boards.greenhouse.io/`**`airbnb`** — the bold part is the token.

---

## 📡 API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/resume/match` | Upload PDF, get ranked job matches |
| `GET`  | `/api/health` | Health check |
| `GET`  | `/health` | ASP.NET health check (used by Docker/k8s) |
| `GET`  | `/swagger` | Interactive API docs |

### `POST /api/resume/match`

**Request** — `multipart/form-data`

| Field | Type | Description |
|-------|------|-------------|
| `file` | PDF | Resume file (max 5 MB) |
| `topN` | int (query) | Number of top matches to return (default `5`) |

**Response** — `200 OK`

```json
{
  "fileName": "resume.pdf",
  "extractedTextLength": 4120,
  "analysis": {
    "skills": ["C#", "Docker", "Kubernetes", "Python"],
    "technologies": ["Docker", "Kubernetes"],
    "education": "Bachelor's Degree",
    "yearsOfExperience": 3,
    "suggestedRoles": ["Backend Engineer", "Software Engineer"],
    "summary": "Candidate with 3+ years of experience..."
  },
  "topMatches": [
    {
      "jobTitle": "Backend Engineer",
      "jobDescription": "...",
      "matchPercentage": 85,
      "matchingSkills": ["C#", "Docker"],
      "missingSkills": ["Terraform"],
      "reason": "Has 3 of 4 required skills...",
      "applyUrl": "https://boards.greenhouse.io/stripe/jobs/12345"
    }
  ]
}
```

---

## 🛠️ Tech Stack

| Layer | Technology |
|-------|------------|
| Web framework | ASP.NET Core 9 |
| PDF extraction | [UglyToad.PdfPig](https://github.com/UglyToad/PdfPig) |
| Job data | [Greenhouse Job Board API](https://developers.greenhouse.io/job-board.html) |
| HTTP client | `IHttpClientFactory` (named client with socket pooling) |
| API docs | Swashbuckle / Swagger UI |
| Frontend | Vanilla HTML + CSS + JavaScript (no framework) |
| Secrets | .NET User Secrets (dev) / Environment variables (prod) |
| Architecture | Clean Architecture (Domain / Application / Infrastructure / API) |

---

## 📁 Project Structure

```
AI-Resume-Matcher/
├── src/
│   ├── ResumeMatcher.Api/
│   │   ├── Controllers/
│   │   │   ├── HealthController.cs
│   │   │   └── ResumeController.cs      ← POST /api/resume/match
│   │   ├── wwwroot/
│   │   │   └── index.html               ← Drag-and-drop web UI
│   │   ├── Program.cs
│   │   └── appsettings.json             ← No secrets here!
│   │
│   ├── ResumeMatcher.Application/
│   │   └── Interfaces/                  ← IPdfTextExtractor, IResumeAnalyzer…
│   │
│   ├── ResumeMatcher.Domain/
│   │   └── Entities/                    ← Job, Resume, JobMatch, ResumeAnalysis
│   │
│   └── ResumeMatcher.Infrastructure/
│       ├── Services/
│       │   ├── PdfPigTextExtractor.cs
│       │   ├── KeywordResumeAnalyzer.cs
│       │   └── SkillBasedJobMatcher.cs
│       └── Repositories/
│           ├── GreenhouseJobRepository.cs  ← Live jobs via REST API
│           └── InMemoryJobRepository.cs    ← Fallback sample data
│
└── .gitignore
```

---

## 🗺️ Roadmap

- [x] PDF text extraction (PdfPig)
- [x] Keyword-based skill detection
- [x] Live Greenhouse job board integration
- [x] Confidence-weighted matching algorithm
- [x] Drag-and-drop web UI
- [ ] OpenAI-powered resume analysis (`IResumeAnalyzer` is already wired)
- [ ] PostgreSQL persistence (swap `InMemoryJobRepository`)
- [ ] Support multiple job boards in one request
- [ ] Docker / docker-compose setup

---

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feat/your-feature`
3. Commit your changes: `git commit -m "feat: add your feature"`
4. Push to the branch: `git push origin feat/your-feature`
5. Open a Pull Request

---

## 📄 License

MIT — see [LICENSE](LICENSE) for details.
