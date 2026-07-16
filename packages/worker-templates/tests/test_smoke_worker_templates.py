"""Smoke tests for worker-templates (Phase 1.5).

Exercises the contract-template ClassVar, the no-op ``PDFRenderer`` constructor,
and the ``TemplateContext`` dataclass. ``TemplateEngine.render`` (reads template
files from disk) and ``PDFRenderer.html_to_pdf`` (lazily imports ``weasyprint``,
pulls native libs) are NOT called.
"""

from worker_templates import ContractGenerator, PDFRenderer, TemplateContext


def test_smoke_templates_surface() -> None:
    assert "employment" in ContractGenerator.TEMPLATES

    renderer = PDFRenderer()

    context = TemplateContext(variables={"a": 1})

    assert renderer is not None
    assert context.locale == "en"
    assert context.variables == {"a": 1}
