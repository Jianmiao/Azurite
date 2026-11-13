# Pure copy projection checks

`ReadShapeFixture.json` is a hand-authored static-shape fixture derived from the pinned current F `AuthoringLines.Read` disassembly and captured schema. All `fixture:` IDs are synthetic and must never be sent to AA. The curve deliberately contains extension data to prove it is retained, not to claim that these tracks pass native curve validation.

The project source-links the new production projection directly. It has no native API stubs, no Core dependency, and does not load or execute AA. A successful projection is only a candidate for future capture/catalog/native round-trip/transaction proof. It is not a complete ScriptData clone or permission to Apply.
