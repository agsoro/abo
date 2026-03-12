# Tools und Plugins

Tools in ABO sind lokale C#-Methoden, die als Plugins fungieren und vom KI-Modell über JSON-basierte Tool-Calls ausgelöst werden können. Sie befinden sich im Verzeichnis `/Tools`.

## Implementierungsdetails

Da ABO reines .NET 10 ohne proprietäre SDKs verwendet, sind Tools Standard-C#-Klassen, die das Interface `IAboTool` implementieren.

### Sicherheit, Ausführung und das "Tools"-Array

Wenn das KI-Modell einen Tool-Call anfordert:
1. Der Orchestrator (oder ein spezialisierter Agent) serialisiert die verfügbaren Tools in das Standard-JSON-`tools`-Array-Format.
2. Das Modell gibt eine Tool-Call-Payload zurück.
3. Die Parameter werden sicher mittels `System.Text.Json` deserialisiert.
4. Die entsprechende C#-Methode wird sicher in der lokalen Umgebung aufgerufen.
5. Das Ergebnis wird serialisiert und als Tool-Message an das Modell zurückgegeben.

Dieser Ansatz vermeidet bewusst die Komplexität des vollen MCP (Model Context Protocol) und garantiert, dass die KI **keinen direkten Zugriff** auf Datenbanken oder interne APIs hat.

---

## Allgemeine Tools

### `get_system_time`
- **Klasse**: `GetSystemTimeTool`
- **Beschreibung**: Gibt die aktuelle UTC-Systemzeit zurück.
- **Parameter**: keine
- **Verwendet von**: HelloWorldAgent, QuizAgent, PmoAgent, EmployeeAgent

---

## Quiz-Tools

### `get_random_question`
- **Klasse**: `GetRandomQuestionTool`
- **Beschreibung**: Ruft eine zufällige Quizfrage aus dem Datenspeicher ab (optional nach Thema gefiltert).
- **Parameter**: `topic` (optional, string)

### `ask_quiz_question`
- **Klasse**: `AskQuizQuestionTool`
- **Beschreibung**: Präsentiert eine Multiple-Choice-Quizfrage formatiert als Markdown.
- **Parameter**: `id` (string), `topic` (string), `options` (array)
- **Wichtig**: Es müssen immer die Felder `id`, `topic` und `options` aus den Quelldaten der Frage weitergegeben werden.

### `add_quiz_question`
- **Klasse**: `AddQuizQuestionTool`
- **Beschreibung**: Fügt eine neue Quizfrage nach ausdrücklicher Benutzerbestätigung in den Datenspeicher ein.
- **Parameter**: `topic`, `question`, `options`, `answer`, `explanation`, `explanationUrl` (optional), `userId`

### `get_quiz_topics`
- **Klasse**: `GetQuizTopicsTool`
- **Beschreibung**: Gibt alle verfügbaren Quizthemen zurück.
- **Parameter**: keine

### `update_quiz_score`
- **Klasse**: `QuizTools` (Score-Update)
- **Beschreibung**: Aktualisiert den Punktestand eines Benutzers. Darf **nur** bei korrekter Antwort aufgerufen werden.
- **Parameter**: `channelId`, `userName`, `topic` (optional)

### `get_quiz_leaderboard`
- **Klasse**: `QuizTools` (Leaderboard)
- **Beschreibung**: Gibt die aktuelle Rangliste des Quiz zurück.
- **Parameter**: `channelId`

### `subscribe_quiz` / `unsubscribe_quiz`
- **Klasse**: `QuizTools`
- **Beschreibung**: Verwaltet stündliche Quiz-Abonnements für einen Kanal.
- **Parameter**: `channelId`, `userName`

### `ask_multiple_choice`
- **Klasse**: `AskMultipleChoiceTool`
- **Beschreibung**: Stellt eine generische Multiple-Choice-Frage (z. B. Lieblingsfarbe). Wird vom `HelloWorldAgent` verwendet.
- **Parameter**: `question`, `options` (array)

---

## PMO- / Prozessmanagement-Tools

### `create_process`
- **Klasse**: `CreateProcessTool`
- **Beschreibung**: Erstellt eine neue BPMN-Prozessdefinition als `.bpmn`-Datei. Validiert das XML vor dem Speichern automatisch.
- **Parameter**: `processId` (string, eindeutig), `bpmnXml` (string, vollständiges BPMN 2.0 XML)
- **Wichtig**: Jeder Knoten, jedes Gateway und jeder Übergang **muss eine eindeutige ID** tragen.

### `update_process`
- **Klasse**: `UpdateProcessTool`
- **Beschreibung**: Aktualisiert eine bestehende BPMN-Prozessdefinition.
- **Parameter**: `processId`, `bpmnXml`

### `check_bpmn`
- **Klasse**: `CheckBpmnTool`
- **Beschreibung**: Prüft, ob ein BPMN-XML-String wohlgeformt und parsebar ist. Sollte **vor dem Speichern** mittels `create_process` oder `update_process` verwendet werden.
- **Parameter**: `bpmnXml`

### `start_project`
- **Klasse**: `StartProjectTool`
- **Beschreibung**: Startet eine neue Projektinstanz auf Basis eines existierenden BPMN-Prozesses. Erstellt Projektverzeichnis, `info.md`, `status.json` und trägt das Projekt in `active_projects.json` ein.
- **Parameter**: `projectId`, `title`, `typeId`, `info`, `initialStepId`, `environmentName`, `parentId` (optional)

### `list_projects`
- **Klasse**: `ListProjectsTool`
- **Beschreibung**: Listet alle aktiven Projekte mit Hierarchie, Typ, aktuellem BPMN-Schritt und Statuslink auf.
- **Parameter**: keine

### `get_open_work`
- **Klasse**: `GetOpenWorkTool`
- **Beschreibung**: Analysiert alle aktiven Projekte und extrahiert strukturierte, handlungsfähige Aufgaben. Zeigt erwartete Rolle und Zustand basierend auf dem BPMN-Fluss.
- **Parameter**: keine

### `upsert_role`
- **Klasse**: `UpsertRoleTool`
- **Beschreibung**: Erstellt oder aktualisiert eine KI-Agentenrolle (Name + System-Prompt). Rollen-IDs müssen mit den im BPMN verwendeten IDs übereinstimmen.
- **Parameter**: `roleId`, `name`, `systemPrompt`

### `get_roles`
- **Klasse**: `GetRolesTool`
- **Beschreibung**: Gibt alle aktuell definierten KI-Rollen und deren Beschreibungen zurück.
- **Parameter**: keine

### `get_environments`
- **Klasse**: `GetEnvironmentsTool`
- **Beschreibung**: Listet alle konfigurierten Umgebungen auf, die für Projekte genutzt werden können (z. B. lokale Verzeichnisse mit Name, Typ und Pfad).
- **Parameter**: keine

---

## Konnektor-Tools (EmployeeAgent)

Diese Tools sind **nur nach einem erfolgreichen `checkout_project`** nutzbar. Alle Pfade sind auf das Verzeichnis der ausgecheckten Projektumgebung beschränkt. Sie befinden sich im Unterordner `/Tools/Connector`.

### `checkout_project`
- **Beschreibung**: Checkt ein laufendes Projekt anhand seiner ID aus und bindet den Umgebungs-Konnektor daran. Voraussetzung für alle Dateisystem- und Shell-Tools.
- **Parameter**: `projectId` (string)
- **Implementiert in**: `EmployeeAgent.HandleCheckoutProjectAsync` (kein eigenes Tool-File)

### `complete_task`
- **Beschreibung**: Markiert die aktuelle Aufgabe im ausgecheckten Projekt als abgeschlossen und aktualisiert den Status.
- **Parameter**: `nextStepId` (optional, string) – ID des nächsten BPMN-Schritts
- **Implementiert in**: `EmployeeAgent.HandleCompleteTaskAsync` (kein eigenes Tool-File)

### `request_ceo_help`
- **Beschreibung**: Stoppt die Arbeit und eskaliert eine Frage/ein Problem an den menschlichen CEO.
- **Parameter**: `message` (string)
- **Implementiert in**: `EmployeeAgent.HandleRequestCeoHelp` (kein eigenes Tool-File)

### `read_file`
- **Klasse**: `ReadFileTool` (`/Tools/Connector/ReadFileTool.cs`)
- **Beschreibung**: Liest den Inhalt einer Datei anhand eines relativen Pfades.
- **Parameter**: `relativePath`

### `write_file`
- **Klasse**: `WriteFileTool` (`/Tools/Connector/WriteFileTool.cs`)
- **Beschreibung**: Schreibt Inhalt in eine Datei (erstellt oder überschreibt).
- **Parameter**: `relativePath`, `content`

### `delete_file`
- **Klasse**: `DeleteFileTool` (`/Tools/Connector/DeleteFileTool.cs`)
- **Beschreibung**: Löscht eine Datei aus dem Projektverzeichnis.
- **Parameter**: `relativePath`

### `list_dir`
- **Klasse**: `ListDirTool` (`/Tools/Connector/ListDirTool.cs`)
- **Beschreibung**: Listet den Inhalt eines Verzeichnisses auf.
- **Parameter**: `relativePath`

### `mkdir`
- **Klasse**: `MkDirTool` (`/Tools/Connector/MkDirTool.cs`)
- **Beschreibung**: Erstellt ein neues Verzeichnis im Projekt.
- **Parameter**: `relativePath`

### `git`
- **Klasse**: `GitTool` (`/Tools/Connector/GitTool.cs`)
- **Beschreibung**: Führt einen Git-Befehl im Projektverzeichnis aus. Das Wort `git` darf **nicht** in den Argumenten enthalten sein.
- **Parameter**: `arguments`

### `dotnet`
- **Klasse**: `DotnetTool` (`/Tools/Connector/DotnetTool.cs`)
- **Beschreibung**: Führt einen .NET CLI-Befehl im Projektverzeichnis aus. Das Wort `dotnet` darf **nicht** in den Argumenten enthalten sein.
- **Parameter**: `arguments`
