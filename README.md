<img width="1347" height="957" alt="image" src="https://github.com/user-attachments/assets/346f09a2-cf38-45b9-835a-e8acfa06ee36" />

# Data Migration Assistant

Data Migration Assistant is a web-based application designed to simplify the migration of spreadsheet data into PostgreSQL databases.

The system helps users:

- Preview Excel datasets
- Infer PostgreSQL schemas
- Validate data quality
- Analyze candidate keys
- Generate normalization proposals
- Generate SQL migration scripts
- Generate GTN scenarios
- Perform AI-assisted reviews
- Interact with a Multi-Agent Migration Assistant

---

## Technologies

- ASP.NET Core
- Blazor Server
- C#
- PostgreSQL
- xUnit
- Claude Code
- Ollama (DeepSeek-Coder-V2)

---

## Architecture

The migration pipeline follows a deterministic-first architecture:

```text
Preview
↓
Schema Inference
↓
Validation
↓
Data Analysis
↓
AI Review
```

Deterministic modules provide trusted facts, while AI modules explain, summarize, and assist users.

---

## Multi-Agent Assistant

The assistant automatically routes questions to specialized agents.

The assistant follows a deterministic-first architecture. Whenever possible, questions are answered directly from trusted application data rather than through an AI model. The General Migration Agent serves as a fallback for broader migration questions.

### Available Agents

- 🧠 Schema Agent
- 🛡 Validation Agent
- 📊 Data Analysis Agent
- 🧩 Normalization Agent
- 🧾 SQL Agent
- 🧬 GTN Agent
- 🤖 General Migration Agent

---

## Example Questions

### 🧠 Schema Agent

- What type is score?
- What candidate keys exist?
- Which columns are nullable?

### 🛡 Validation Agent

- Can I proceed with migration?
- What validation warnings exist?
- Are there duplicate risks?

### 📊 Data Analysis Agent

- Should username be the primary key?
- What risks do you see?
- What recommendations do you have?

### 🧩 Normalization Agent

- Why was this normalization proposed?
- What tables were generated?

### 🧾 SQL Agent

- What migration SQL was generated?
- Show the generated migration SQL.
- What tables will be created?

### 🧬 GTN Agent

- How do I generate a GTN seed script?
- What GTN seed SQL was generated?
- How many GTN scenarios were generated?

---

## Sample Data

The repository includes example datasets in the `/sample-data` folder.

You can upload these files directly into the application to reproduce the examples shown in the documentation.

### Included datasets

- `users.xlsx` – schema inference, candidate key analysis, normalization, and Multi-Agent Assistant examples.
- `validation-rules.xlsx` – validation and data quality examples.

---

## Running the Project

### 1. Start Ollama

```bash
ollama run deepseek-coder-v2
```

### 2. Run the Blazor application

```bash
dotnet run --project DataMigrationAssistant.Blazor
```

### 3. Open the application

Open the URL displayed by ASP.NET Core in your browser.

---

## Features

- Excel file preview
- PostgreSQL schema inference
- Candidate key detection
- Validation warnings
- Deterministic data analysis
- AI-powered review
- Schema normalization proposals
- SQL migration generation
- GTN scenario generation
- Multi-Agent Migration Assistant

---

## Testing

The project includes extensive automated tests covering:

- Schema inference
- Validation rules
- Data analysis
- AI review validation
- Agent routing
- Agent responses
- SQL generation
- GTN generation

At the time of writing, the project contains more than **1,380 automated tests**.

---

## AI-Assisted Development

This project was developed using an AI-assisted workflow.

AI tools were used for:

- Architecture design
- Code generation
- Refactoring
- Test generation
- Debugging
- Documentation

The project demonstrates a deterministic-first approach where AI assists users while deterministic modules remain the source of truth.
