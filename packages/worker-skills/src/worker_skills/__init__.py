"""Das Vokabular: welche Wörter dasselbe meinen.

Der Rest eines „Skill-Graphen", der nach **ADR-0022** übrig bleibt — und die
einzige Aufgabe eines solchen Graphen, die niemandem etwas unterstellt: dafür
sorgen, dass zwei Menschen, die dasselbe meinen, auch dasselbe Wort benutzen.

Der Bedarf ist nicht theoretisch. Der Abgleich aus Sub-step 6.2 vergleicht
buchstabengetreu (ohne Rücksicht auf Groß-/Kleinschreibung). Eine Stelle, die
„PostgreSQL" verlangt, und ein Profil, in dem „Postgres" steht, ergaben ein ✗ —
eine Lücke, die es nicht gibt, gezeigt an einen Menschen, der sich deshalb
womöglich nicht bewirbt.

**Die Grenze, die alles trägt: hier wird umbenannt, nicht abgeleitet.**

    erlaubt:   „Postgres" und „PostgreSQL" sind dasselbe Wort
    verboten:  „React" heißt, du kannst auch JavaScript

Das erste ist eine Aussage über Sprache. Das zweite ist eine Aussage über einen
**Menschen** — es schreibt ihm eine Fähigkeit zu, die er nicht genannt hat, an
einer Stelle, an der er nicht widersprechen kann. Wer React kann und JavaScript
nennen will, nennt es.

Daraus folgt: kein Niveau, kein Gewicht, keine Rangfolge, keine Verwandtschaft.
Nichts, woraus sich später eine Zahl bauen ließe (ADR-0023).

Und: **es lehnt nie etwas ab und erfindet nie etwas.** Was das Vokabular nicht
kennt, bleibt genau so stehen, wie es getippt wurde. Eine Liste erlaubter
Fähigkeiten wäre eine Behauptung darüber, welche Arbeit es gibt — und sie läge
bei jeder neuen Technologie und bei jedem Beruf außerhalb der IT falsch.
"""

from __future__ import annotations

__all__ = ["ALIASES", "CANONICAL_NAMES", "canonical", "canonical_all"]

#: Kanonischer Name → seine Schreibweisen.
#:
#: Gepflegt im Repository, per Pull Request: nachvollziehbar und widersprechbar.
#: Eine Liste, die sich selbst aus den Daten füttert („diese Wörter kommen oft
#: zusammen vor"), wäre wieder eine Auswertung über Menschen.
#:
#: Bewusst klein gehalten. Jeder Eintrag ist eine Behauptung, dass zwei Wörter
#: **dasselbe** meinen — bei Zweifel gehört er nicht hinein. Und die Liste ist
#: nicht auf IT beschränkt: der Transfermarkt kennt Pflege, Handwerk und
#: Vertrieb, und dort verschreibt man sich genauso.
ALIASES: dict[str, tuple[str, ...]] = {
    # Sprachen und Laufzeiten
    "JavaScript": ("js", "java script", "ecmascript"),
    "TypeScript": ("ts", "type script"),
    "Python": ("python3", "python 3", "py"),
    "C#": ("c sharp", "csharp", "c-sharp"),
    "C++": ("cpp", "c plus plus"),
    "Go": ("golang",),
    "Node.js": ("node", "nodejs", "node js"),
    ".NET": ("dotnet", "dot net", "net core", ".net core"),
    # Datenbanken
    # „postgresql" steht hier NICHT: der kanonische Name wird ohnehin erkannt,
    # und ihn zusätzlich als eigene Schreibweise zu führen war der erste Fehler,
    # den `test_a_name_is_never_listed_as_its_own_alias` gefunden hat.
    "PostgreSQL": ("postgres", "psql", "postgre"),
    "MySQL": ("my sql",),
    "MongoDB": ("mongo", "mongo db"),
    "Microsoft SQL Server": ("mssql", "sql server", "ms sql"),
    # Werkzeuge und Betrieb
    "Kubernetes": ("k8s", "kubernets"),
    "Docker": ("docker engine",),
    "CI/CD": ("cicd", "ci cd", "continuous integration"),
    "Amazon Web Services": ("aws",),
    "Microsoft Azure": ("azure",),
    "Google Cloud": ("gcp", "google cloud platform"),
    # Rahmenwerke
    "React": ("react.js", "reactjs", "react js"),
    "Vue.js": ("vue", "vuejs"),
    "Angular": ("angular.js", "angularjs"),
    # Außerhalb der IT — der Transfermarkt ist nicht nur für Entwickler da.
    "Buchhaltung": ("buchführung", "rechnungswesen"),
    "Kundenbetreuung": ("kundenservice", "kundendienst", "customer support"),
    "Projektleitung": ("projektmanagement", "project management"),
    "Altenpflege": ("seniorenpflege", "altenpflegerin", "altenpfleger"),
    "Elektroinstallation": ("elektrik", "elektroinstallateur"),
}

#: Die kanonischen Namen selbst — zum Nachschlagen und für die Anzeige.
CANONICAL_NAMES: tuple[str, ...] = tuple(sorted(ALIASES))

# Nachschlagetabelle, einmal gebaut: Schreibweise (klein) → kanonischer Name.
# Der kanonische Name zeigt auf sich selbst, damit er nicht durchs Raster fällt,
# wenn ihn jemand direkt schreibt.
_BY_SPELLING: dict[str, str] = {}
for _name, _spellings in ALIASES.items():
    _BY_SPELLING[_name.casefold()] = _name
    for _spelling in _spellings:
        _BY_SPELLING[_spelling.casefold()] = _name


def canonical(skill: str) -> str:
    """Die bekannte Schreibweise — oder unverändert, was hereinkam.

    Unbekanntes bleibt stehen. Das ist keine Nachlässigkeit, sondern der Kern:
    das Vokabular kennt nicht alle Arbeit, die es gibt, und darf so tun, als
    kennte es sie.
    """
    return _BY_SPELLING.get(skill.strip().casefold(), skill.strip())


def canonical_all(skills: list[str] | tuple[str, ...]) -> list[str]:
    """Wie `canonical`, aber für eine Liste — Leeres fällt weg.

    **Nicht** entdoppelt: das gehört ins `Skills`-Wertobjekt des jeweiligen
    Dienstes, und zwar NACH dem Umbenennen. Andersherum würde aus
    „Postgres, PostgreSQL" zweimal derselbe Eintrag.
    """
    named = [canonical(skill) for skill in skills]
    return [skill for skill in named if skill]
