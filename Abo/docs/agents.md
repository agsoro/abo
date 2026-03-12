# Agent-Übersicht

ABO unterstützt eine spezialisierte Multi-Agenten-Architektur. Anstatt eines einzigen monolithischen Bots koordiniert ein **Supervisor** spezialisierte Agenten.

## Agent Supervisor

Der `AgentSupervisor` ist der Einstiegspunkt für alle Benutzerinteraktionen. Er analysiert mithilfe des LLM die Absicht des Benutzers und wählt anhand von `Name` und `Description` den geeignetsten Agenten aus.

## Registrierte Agenten

Agenten in ABO sind spezialisierte Rollen mit spezifischen Anweisungen, Tools und Einschränkungen. Alle Agenten implementieren das Interface `IAgent`.

---

### HelloWorldAgent
- **Klasse**: `Abo.Agents.HelloWorldAgent`
- **Beschreibung**: Ein einfacher Assistent für allgemeine Begrüßungen, Zeitabfragen und grundlegende Tests.
- **Fähiges Modell erforderlich**: Nein (`RequiresCapableModel = false`)
- **Tools**: 
  - `get_system_time` – Gibt die aktuelle Systemzeit aus.
  - `ask_multiple_choice` – Stellt Multiple-Choice-Fragen zu persönlichen Präferenzen (z. B. Lieblingsfarbe).
- **Verwendung**: Wird ausgewählt, wenn der Benutzer die Uhrzeit fragt oder allgemeine Begrüßungen äußert.
- **Hinweis**: Dieser Agent ist **kein** Quiz-Agent. Falls Quiz-Anfragen fälschlicherweise hier ankommen, werden diese nicht bearbeitet.

---

### QuizAgent
- **Klasse**: `Abo.Agents.QuizAgent`
- **Beschreibung**: Ein spezialisierter Agent für Tech- und Nerd-Trivia, Abonnements und Ranglisten.
- **Fähiges Modell erforderlich**: Nein (`RequiresCapableModel = false`)
- **Tools**:
  - `get_random_question` – Ruft eine zufällige Quizfrage aus dem Datenspeicher ab (optional nach Thema gefiltert).
  - `ask_quiz_question` – Präsentiert eine Frage dem Benutzer (formatiertes Markdown mit `id`, `topic` und `options`).
  - `add_quiz_question` – Fügt eine neue Quizfrage nach ausdrücklicher Benutzerbestätigung hinzu.
  - `get_quiz_topics` – Gibt alle verfügbaren Themengebiete zurück.
  - `update_quiz_score` – Aktualisiert den Punktestand **nur bei korrekter Antwort**.
  - `get_quiz_leaderboard` – Zeigt die aktuelle Rangliste an.
  - `subscribe_quiz` / `unsubscribe_quiz` – Verwaltet stündliche Quiz-Abonnements für einen Kanal.
  - `get_system_time` – Gibt die aktuelle Systemzeit aus.
- **Regeln**:
  - Punkte werden **ausschließlich bei richtigen Antworten** vergeben. `update_quiz_score` darf niemals für eine falsche Antwort aufgerufen werden.
  - Für jede Antwort ist eine verständliche Erklärung **Pflicht** (inkl. Link falls `explanationUrl` vorhanden).
  - Neue Fragen werden **nur nach expliziter Benutzerbestätigung** gespeichert (kein automatisches Speichern).
  - Es gibt kein `check_quiz_answer`-Tool – die Antwortauswertung erfolgt durch das Modell selbst anhand des Gesprächsverlaufs.
  - Beim Aufruf von `add_quiz_question` muss die **Mattermost User ID** aus dem `[CONTEXT]` als `userId` übergeben werden.
  - Für alle Tools müssen **Channel ID** und **User Name** aus dem `[CONTEXT]` verwendet werden.

---

### PmoAgent (Project Management Office)
- **Klasse**: `Abo.Agents.PmoAgent`
- **Beschreibung**: Der PMO-Lead-Agent. Verantwortlich für das Entwerfen von BPMN-Prozessen, das Instanziieren von Projekten und das Verwalten von Rollen.
- **Fähiges Modell erforderlich**: Ja (`RequiresCapableModel = true`)
- **Tools**:
  - `create_process` – Erstellt eine neue BPMN-Prozessdefinition.
  - `update_process` – Aktualisiert eine bestehende BPMN-Prozessdefinition.
  - `start_project` – Startet eine neue Projektinstanz basierend auf einem existierenden Prozess.
  - `list_projects` – Listet alle aktiven Projekte und deren aktuellen Status auf.
  - `get_open_work` – Zeigt offene Arbeitspakete über alle Projekte hinweg.
  - `upsert_role` – Erstellt oder aktualisiert eine KI-Agentenrolle mit System-Prompt.
  - `get_roles` – Listet alle definierten Rollen auf.
  - `get_system_time` – Gibt die aktuelle Systemzeit aus.
- **Arbeitsweise**: Folgt dem PDCA-Zyklus (Plan → Do → Check → Act). Entwirft Prozesse, definiert Rollen und gibt die Ausführungsarbeit an den `EmployeeAgent` weiter.
- **Regeln**:
  - Jeder Knoten, jedes Gateway und jeder Übergang im BPMN **muss eine eindeutige ID** tragen.
  - Vor dem Erstellen neuer Rollen immer `get_roles` prüfen.
  - Der PMO-Agent führt **keine direkte Aufgabenarbeit** aus – er delegiert an instanziierte BPMN-Flows.
  - Benutzer können Prozesse im Web-UI unter `/processes/index.html` visualisieren.

---

### EmployeeAgent
- **Klasse**: `Abo.Agents.EmployeeAgent`
- **Beschreibung**: Der generische Mitarbeiter-Agent. Übernimmt konkrete Aufgaben aus laufenden Projekten und führt sie eigenständig aus.
- **Fähiges Modell erforderlich**: Ja (`RequiresCapableModel = true`)
- **Lebenszyklus-Tools**:
  - `checkout_project` – Bindet einen sicheren Konnektor an eine Projektumgebung (muss vor Dateisystem-/Shell-Tools aufgerufen werden).
  - `complete_task` – Markiert die aktuelle Aufgabe als abgeschlossen und rückt den BPMN-Schritt vor. Optionaler Parameter: `nextStepId`.
  - `request_ceo_help` – Eskaliert ein Problem an den menschlichen CEO.
- **Globale Informations-Tools**: `list_projects`, `get_open_work`, `get_system_time`, `get_roles`, `get_environments`
- **Konnektor-Tools** (nur nach `checkout_project` nutzbar):
  - `read_file` – Datei lesen.
  - `write_file` – Datei schreiben/erstellen.
  - `delete_file` – Datei löschen.
  - `list_dir` – Verzeichnisinhalt anzeigen.
  - `mkdir` – Neues Verzeichnis erstellen.
  - `git` – Git-Befehle ausführen (ohne das Wort `git`).
  - `dotnet` – .NET CLI-Befehle ausführen (ohne das Wort `dotnet`).
- **Sicherheit**: Alle Dateisystem- und Shell-Operationen sind auf das Verzeichnis der ausgecheckten Projektumgebung beschränkt. Pfade außerhalb sind nicht erreichbar.
- **Workflow**:
  1. `list_projects` oder `get_open_work` aufrufen, um offene Arbeit zu finden.
  2. `checkout_project` mit der `projectId` aufrufen.
  3. Projektrolle und Aufgabe aus `info.md` oder BPMN lesen.
  4. Arbeit mit Konnektor-Tools ausführen.
  5. `complete_task` aufrufen (ggf. mit `nextStepId`).

## Implementierung

Alle Agenten implementieren das Interface `IAgent` (`Abo.Agents.IAgent`) und werden als transiente Services in `Program.cs` registriert. Der `AgentSupervisor` wählt dynamisch den passenden Agenten per LLM-gestützter Intentionsanalyse anhand von `Name` und `Description` jedes Agenten aus.
