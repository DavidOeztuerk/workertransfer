---
id: Q-0054
gate: G-0015
kind: main
rank: B
state: in_progress
dod:
  - Neue CLAUDE.md höchstens etwa 20 KB; enthält nur, was jede Session braucht: was WT ist, Layout, wichtigste Befehle, Architektur-Regeln kurz, je eine verbindliche Zeile pro tragender Regel mit Verweis, Conventions that bite, Docs-Karte
  - Jede
  - Inhalt verschoben, nicht umgeschrieben; Sprache je Abschnitt unverändert
  - Jede Regel, die ein Agent immer einhalten muss (Sicherheit, Einwilligung, Löschung, Fehlerformat, Branches), steht weiter als kurze Regel in der CLAUDE.md
  - Skills unter .claude/skills/<thema>/SKILL.md mit präziser description; wt-nachweis und wt-tore erweitert statt gedoppelt
  - AGENTS.md auf Doppelungen und Widersprüche geprüft, nur das Nötigste angeglichen
  - .claude/worktrees/ in .gitignore
  - Eine Warden-Runde: keine verbindliche Regel verloren oder nur in einem Skill versteckt; descriptions präzise
  - develop vor und nach dem Merge grün
scope:
  include: ["CLAUDE.md", "AGENTS.md", ".gitignore", ".claude/skills/**", "docs/**"]
  exclude: []
estimate: { session_pct: 35, weekly_pct: 6, gold_usd: 0 }
boss: "Überschriften-Abgleich alt → neu ohne Lücke, CLAUDE.md ≤ ~20 KB, CI auf develop grün"
---
# WT – CLAUDE.md auf den Kern, Aufgabenwissen in Skills, Geschichte nach docs
