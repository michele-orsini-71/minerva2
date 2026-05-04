# 2. mission in india

## 2025-06-05

A message from the dark web, intercepted on a dismissed satellite channel.
The full payload below is intentionally large enough to push this section
above the default TargetChunkSize, so the chunker routes it through its
oversized-section split path — which is where the orphan-header bug used
to manifest. Without the fix, the H1 above would land as its own chunk
(content == "# 2. mission in india") because greedy packing flushes the
current pack before handing the oversized section off to the recursive
splitter.

```
ZIRCON-SINGH-MURDUR                           -> from Rahim
PRIORITY 4 // SIGMA BRIDGE                    -> high importance, not max // dismissed satellite channel
SEVEN-PATHS CROSS IN SILENCE                  -> exit route — escape without official support

Z04-DELTA // RAH-92-PAH                       -> operational area Ladakh in India // Rahim PAH(algam) capture in India
SEED: VERTEBRA-1918                           -> "broken spine" — i.e. harsh detention
WATERMARK: BLOOD BETWEEN ICE AND DUST         -> climate of the area (cold, high altitude)
SENDER: WHITE FINGER / PRISON-KS-117          -> Rahim's code name / identifies a maximum-security prison in Ladakh

The map is held in the eye of the blind.     -> the prisoner is in isolation
The wall breathes on the western side.       -> structural weak point
A shadow remembers what the lock forgets.    -> insider help available
The third bell rings only once.              -> single window for action
```

Closing context. The dark-web fragment is sufficient to exceed any
reasonable default TargetChunkSize, ensuring the section is routed
through SplitOversizedSection, where this regression test verifies that
the preceding H1 does not survive as a header-only chunk.
