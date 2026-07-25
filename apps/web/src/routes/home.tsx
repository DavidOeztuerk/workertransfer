import { Button, Card } from "@workertransfer/ui";

const foundations = [
  {
    title: "Du entscheidest",
    text: "Sichtbarkeit, Kontakte, Dokumente und jedes Angebot bleiben unter deiner Kontrolle."
  },
  {
    title: "Echte Nachweise",
    text: "Projekterfahrung und verknüpfte Quellen werden transparent gezeigt – nur nach Freigabe."
  },
  {
    title: "Human in control",
    text: "KI erstellt Entwürfe und Orientierung; Bewerbungen, Nachrichten und Verträge werden geprüft."
  }
];

export function HomeRoute() {
  return (
    <main>
      <section className="hero" aria-labelledby="hero-title">
        <div className="hero__navigation">
          <a className="brand" href="/" aria-label="WorkerTransfer Startseite">
            worker<span>transfer</span>
          </a>
          <nav aria-label="Hauptnavigation">
            <a href="#principles">Prinzipien</a>
            <a href="#roadmap">Produkt</a>
            <Button variant="secondary">Frühzugang vormerken</Button>
          </nav>
        </div>

        <div className="hero__content">
          <p className="eyebrow">Talent mobility, menschenzentriert</p>
          <h1 id="hero-title">Neue Arbeit soll sich wie eine selbstbestimmte Entscheidung anfühlen.</h1>
          <p className="hero__lead">
            WorkerTransfer verbindet Bewerbung, direkte Ansprache und faire Wechselprozesse –
            mit nachvollziehbarer KI-Unterstützung statt Black-Box-Entscheidungen.
          </p>
          <div className="hero__actions">
            <Button>Als Arbeitnehmer starten</Button>
            <Button variant="secondary">Als Unternehmen entdecken</Button>
          </div>
        </div>

        <div className="hero__orb" aria-hidden="true" />
      </section>

      <section className="principles" id="principles" aria-labelledby="principles-title">
        <p className="eyebrow">Unser Ausgangspunkt</p>
        <h2 id="principles-title">Vertrauen ist kein Feature. Es ist die Architektur.</h2>
        <div className="principles__grid">
          {foundations.map((foundation, index) => (
            <Card className="principle" key={foundation.title}>
              <span className="principle__number">0{index + 1}</span>
              <h3>{foundation.title}</h3>
              <p>{foundation.text}</p>
            </Card>
          ))}
        </div>
      </section>

      <section className="roadmap" id="roadmap" aria-labelledby="roadmap-title">
        <div>
          <p className="eyebrow">In Entwicklung</p>
          <h2 id="roadmap-title">Eine Plattform, die mit einer klaren ersten Grundlage wächst.</h2>
        </div>
        <p>
          Zuerst entstehen sichere Identitäten, Profile und Einwilligungen. Danach folgen Jobs,
          Bewerbungen, nachvollziehbare GitHub-Nachweise und ein einvernehmlicher Transfer-Flow.
        </p>
      </section>
    </main>
  );
}
