# Embedded XSD schemas

These are the FA(3) invoice schema and its complete import chain, vendored so that
`XmlValidator` never dereferences a `schemaLocation` at runtime.

## Why they are vendored

`schemat_FA(3)_v1-0E.xsd` opens with an **absolute** import:

```xml
<xsd:import namespace="http://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/01/05/eD/DefinicjeTypy/"
            schemaLocation="http://crd.gov.pl/.../StrukturyDanych_v10-0E.xsd"/>
```

Left to resolve itself, every invoice validation performed a **plaintext HTTP** fetch from
`crd.gov.pl`. That made validation impossible offline, tied it to the availability of a
government host, and — because the transport was unauthenticated — let anyone on the network
path substitute the schema that decides whether an invoice is valid.

All four files are now registered explicitly in `XmlValidator.SchemaResources`, and both the
schema set and the reader have `XmlResolver = null` with `DtdProcessing = Prohibit`.

## The chain

Leaf first, which is the order they are registered in:

| File | Provides | Origin |
|---|---|---|
| `KodyKrajow_v10-0E.xsd` | `TKodKraju` (254 ISO country codes) | `http://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/01/05/eD/DefinicjeTypy/KodyKrajow_v10-0E.xsd` |
| `ElementarneTypyDanych_v10-0E.xsd` | `TNaturalny`, `TData`, `TKwota2`, … | `http://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/01/05/eD/DefinicjeTypy/ElementarneTypyDanych_v10-0E.xsd` |
| `StrukturyDanych_v10-0E.xsd` | `TAdres`, `TOsobaFizyczna`, … | vendored from `KSeF.Client.Tests.Core/Schemas/` in `CIRFMF/ksef-client-csharp` |
| `schemat_FA(3)_v1-0E.xsd` | the FA(3) invoice structure | already present in this repository |

`ElementarneTypyDanych` includes `KodyKrajow` by *relative* path; the other two links are
absolute URLs.

## Integrity

Retrieved 2026-07-26. SHA-256:

```
48be2a9f181d7ff80f185c62491ba12604c5cacbbe21af8e2aaaf2c585bbd214  KodyKrajow_v10-0E.xsd
8daf4d3771de200b26b697294cc906a2add3de9acfbbd97f4b1bd4fc0e5ecb2f  ElementarneTypyDanych_v10-0E.xsd
1137ce6e3c11c2b9ef3f05e4e72d6dcd6b4fa94908ea558f2ba15de0259bb2aa  StrukturyDanych_v10-0E.xsd
b646b6b525f51adf1bb2545f111fc8ca6e7aa6dd2f98948f1667d3695c06d958  schemat_FA(3)_v1-0E.xsd
```

Verify with `sha256sum -c` after any refresh.

## Refreshing

Revisit whenever the FA schema version changes (currently **FA(3) v1-0E**). Re-fetch the whole
chain together — a partial update will not fail loudly, because `XmlSchemaSet.Compile()` only
catches types that go missing, not types that silently change their constraints.

`XmlValidatorSecurityTests` guards the invariants: all four resources embedded, the sample
invoice validating offline, and a bogus `KodKraju` still being rejected (which proves the
imported types are genuinely enforced rather than skipped).
