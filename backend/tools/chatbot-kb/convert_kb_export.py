"""Converts the reviewed chatbot knowledge-base export (T0) into the runtime KB format (ADR 0026).

Usage:  python -I convert_kb_export.py <export-dir> <output-dir>

<export-dir> holds intents/*.json, flows/*.json, variants/*.json and criteria/fallback-escalation.json, as exported
from the review pages. <output-dir> is the KB folder (backend/src/TaskFlow.Infrastructure/Chatbot/KnowledgeBase):
settings.json, common.json and intents/<Intent>.json are rewritten; synonyms.json is left alone (hand-maintained).
The output is deterministic, so re-running it on the same export changes nothing.
"""
import json
import sys
from pathlib import Path

LOCALES = ["en", "es"]
VARIANTS = 5


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def convert_step(step, variants_dir):
    variants = read(variants_dir / f"{step['id']}.json")
    text = {}
    for locale in LOCALES:
        items = variants[locale]
        if len(items) != VARIANTS:
            raise SystemExit(f"step {step['id']}: expected {VARIANTS} '{locale}' variants, found {len(items)}")
        if items[0] != step["text"][locale]:
            raise SystemExit(f"step {step['id']}: '{locale}' variant 0 differs from the approved text")
        text[locale] = items
    out = {"text": text}
    if step["quickReplies"]:
        replies = []
        for q in step["quickReplies"]:
            reply = {"id": q["id"], "label": {k: q["label"][k] for k in LOCALES}}
            for key in ("next", "url"):
                if q.get(key):
                    reply[key] = q[key]
            replies.append(reply)
        out["quickReplies"] = replies
    for key in ("expectsText", "end", "outcome"):
        if step.get(key):
            out[key] = step[key]
    return out


def main(export, output):
    export, output = Path(export), Path(output)
    variants_dir = export / "variants"
    criteria = read(export / "criteria" / "fallback-escalation.json")
    write(output / "settings.json", {
        "version": "1.0.0",
        "locales": LOCALES,
        "minUtterancesPerLocale": 30,
        "confidenceThreshold": criteria["confidenceThreshold"],
        "minMargin": criteria["minMargin"],
        "maxConsecutiveFallbacks": criteria["maxConsecutiveFallbacks"],
        "sessionIdleMinutes": criteria["sessionIdleMinutes"],
        "startStep": "start",
        "fallbackStep": "fallback",
        "escalateStep": "escalate",
        "outOfScopeIntent": "OutOfScope",
        # Placeholder names only: the values come from configuration (Chatbot:SupportContact, Chatbot:HelpUrl).
        "links": {"support": "", "help": ""},
    })

    intents = {}
    for path in sorted((export / "intents").glob("*.json")):
        data = read(path)
        intents[data["intent"]] = data

    for path in sorted((export / "flows").glob("*.json")):
        flow = read(path)
        steps = {s["id"]: convert_step(s, variants_dir) for s in flow["steps"]}
        if flow["slug"] == "common":
            write(output / "common.json", {"steps": steps})
            continue
        intent = intents[flow["intent"]]
        utterances = {
            locale: [c["text"] for c in intent["cases"] if c["locale"] == locale] for locale in LOCALES
        }
        write(output / "intents" / f"{intent['intent']}.json", {
            "intent": intent["intent"],
            "description": intent["description"],
            "triggers": intent["triggers"],
            "notTriggers": intent["notTriggers"],
            "confusedWith": intent["confusedWith"],
            "utterances": utterances,
            "entryStep": flow["entryStep"],
            "steps": steps,
        })


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2])
