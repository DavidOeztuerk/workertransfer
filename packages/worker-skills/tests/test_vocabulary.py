"""Die Regeln des Vokabulars — und die Grenze, die es nie überschreiten darf."""

from __future__ import annotations

import pytest
from worker_skills import ALIASES, CANONICAL_NAMES, canonical, canonical_all


class TestRenaming:
    def test_a_known_spelling_becomes_the_known_name(self) -> None:
        assert canonical("postgres") == "PostgreSQL"
        assert canonical("k8s") == "Kubernetes"
        assert canonical("golang") == "Go"

    def test_case_and_space_do_not_matter(self) -> None:
        assert canonical("  POSTGRES  ") == "PostgreSQL"
        assert canonical("Java Script") == "JavaScript"

    def test_the_canonical_name_itself_is_recognised(self) -> None:
        # Sonst fiele genau die richtige Schreibweise durchs Raster.
        for name in CANONICAL_NAMES:
            assert canonical(name) == name

    def test_what_it_does_not_know_stays_exactly_as_typed(self) -> None:
        """Der Kern, nicht eine Nachlässigkeit.

        Das Vokabular kennt nicht alle Arbeit, die es gibt. Eine Liste erlaubter
        Fähigkeiten wäre eine Behauptung darüber, welche Arbeit existiert — und
        sie läge bei jeder neuen Technologie und bei jedem Beruf außerhalb der
        IT falsch.
        """
        assert canonical("Hufbeschlag") == "Hufbeschlag"
        assert canonical("Rust") == "Rust"

    def test_it_never_returns_something_nobody_typed(self) -> None:
        # Kein Erfinden: die Antwort ist entweder ein bekannter Name oder das,
        # was hereinkam.
        for typed in ("Rust", "postgres", "völlig neues Ding"):
            assert canonical(typed) in {*CANONICAL_NAMES, typed.strip()}


class TestLists:
    def test_it_renames_every_entry_and_drops_the_empty_ones(self) -> None:
        assert canonical_all(["postgres", "  ", "k8s"]) == ["PostgreSQL", "Kubernetes"]

    def test_it_does_not_deduplicate(self) -> None:
        """Absichtlich nicht — die Reihenfolge trägt.

        Entdoppelt wird im `Skills`-Wertobjekt des jeweiligen Dienstes, und zwar
        NACH dem Umbenennen. Täte es diese Funktion vorher, wäre das Ergebnis
        von der Reihenfolge abhängig; täte es sie hier, hätte der Dienst zwei
        Stellen, an denen entdoppelt wird, und eine davon liefe irgendwann weg.
        """
        assert canonical_all(["Postgres", "PostgreSQL"]) == ["PostgreSQL", "PostgreSQL"]


class TestTheTableIsSound:
    def test_no_spelling_points_at_two_names(self) -> None:
        """Sonst hinge das Ergebnis an der Reihenfolge des Nachschlagens.

        Und zwar unsichtbar: die Tabelle sähe richtig aus, und je nachdem,
        welcher Eintrag zuletzt gebaut wurde, hieße dasselbe Wort mal so und mal
        so.
        """
        seen: dict[str, str] = {}
        for name, spellings in ALIASES.items():
            for spelling in spellings:
                key = spelling.casefold()
                assert key not in seen, f"{spelling!r} zeigt auf {seen.get(key)!r} UND auf {name!r}"
                seen[key] = name

    def test_no_name_is_also_an_alias_of_another(self) -> None:
        # „X heißt eigentlich Y, und Y heißt eigentlich Z" ist eine Kette, und
        # Ketten haben Zyklen. Hier gibt es genau einen Schritt.
        every_alias = {
            spelling.casefold() for spellings in ALIASES.values() for spelling in spellings
        }
        for name in ALIASES:
            assert name.casefold() not in every_alias, f"{name!r} ist Name UND Alias"

    def test_a_name_is_never_listed_as_its_own_alias(self) -> None:
        for name, spellings in ALIASES.items():
            assert name.casefold() not in {s.casefold() for s in spellings}

    def test_renaming_is_stable(self) -> None:
        # Zweimal anwenden muss dasselbe ergeben wie einmal. Sonst hinge das
        # Ergebnis davon ab, wie oft ein Wert durch die Schicht gelaufen ist —
        # und das tut er beim Lesen aus der Datenbank ein weiteres Mal.
        for spelling in (s for spellings in ALIASES.values() for s in spellings):
            once = canonical(spelling)
            assert canonical(once) == once


class TestTheLineItMustNotCross:
    """ADR-0023: das Vokabular benennt um, es leitet nichts ab.

    Diese Tests prüfen keine Funktion, sondern eine Entscheidung. Sie werden
    rot, wenn jemand dem Vokabular ein Niveau, ein Gewicht oder eine
    Verwandtschaft beibringt — also genau dann, wenn aus der
    Umbenennungstabelle wieder etwas wird, aus dem sich eine Zahl bauen lässt.
    """

    def test_the_table_maps_words_to_words_and_nothing_else(self) -> None:
        for name, spellings in ALIASES.items():
            assert isinstance(name, str)
            assert isinstance(spellings, tuple)
            assert all(isinstance(spelling, str) for spelling in spellings)

    def test_the_module_exposes_no_notion_of_level_weight_or_kinship(self) -> None:
        import worker_skills

        forbidden = ("level", "weight", "score", "rank", "implies", "parent", "broader")
        exposed = [name for name in dir(worker_skills) if not name.startswith("_")]

        for name in exposed:
            assert not any(word in name.lower() for word in forbidden), (
                f"{name!r} klingt nach Ableitung. Das Vokabular benennt um "
                "(ADR-0023) — eine Verwandtschaft ist eine Aussage über einen "
                "Menschen, keine über ein Wort."
            )

    @pytest.mark.parametrize(
        ("skill", "not_implied"),
        [("React", "JavaScript"), ("Kubernetes", "Docker"), ("TypeScript", "JavaScript")],
    )
    def test_it_does_not_add_what_someone_did_not_say(self, skill: str, not_implied: str) -> None:
        # Verlockend und in der Praxis oft richtig — und trotzdem verboten: es
        # schreibt jemandem eine Fähigkeit zu, die er nicht genannt hat, an
        # einer Stelle, an der er nicht widersprechen kann.
        assert canonical_all([skill]) == [skill]
        assert not_implied not in canonical_all([skill])
