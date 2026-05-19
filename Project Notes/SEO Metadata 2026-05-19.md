# SEO Metadata 2026-05-19

The shared MVC layout supports page-level search metadata through `ViewData`:

- `SeoTitle` overrides the rendered `<title>` value; otherwise `Title` is suffixed with `TryOutSpot`.
- `MetaDescription` renders the page description and social description.
- `CanonicalPath` or `CanonicalUrl` renders a canonical link and `og:url`.
- `OpenGraphTitle`, `OpenGraphDescription`, `OpenGraphType`, and `OpenGraphImage` override social preview defaults.
- `Robots` renders a page-specific robots directive only when explicitly set.

The splash page now opts into title, description, canonical, Open Graph, Twitter card, and JSON-LD `WebSite` / `Organization` metadata.
