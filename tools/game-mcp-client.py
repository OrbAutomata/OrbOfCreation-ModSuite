#!/usr/bin/env python3
"""Dependency-free CLI client for the suite's perf-debug game MCP server."""

from __future__ import annotations

import argparse
import base64
import json
import math
import pathlib
import sys
import threading
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Any


LATEST_PROTOCOL = "2025-11-25"
DEFAULT_URL = "http://127.0.0.1:19106/mcp"


@dataclass
class McpResponse:
    status: int
    headers: dict[str, str]
    body: dict[str, Any] | None


class GameMcpClient:
    def __init__(self, url: str, transcript: pathlib.Path | None) -> None:
        self.url = url
        self.protocol = LATEST_PROTOCOL
        self.next_id = 1
        self.transcript = transcript
        self._request_id_lock = threading.Lock()
        self._transcript_lock = threading.Lock()

    def initialize(self) -> dict[str, Any]:
        initialized = require_result(
            self.request(
                "initialize",
                {
                    "protocolVersion": LATEST_PROTOCOL,
                    "capabilities": {},
                    "clientInfo": {"name": "orb-game-mcp-cli", "version": "2"},
                },
                include_protocol_header=False,
            )
        )
        negotiated = initialized.get("protocolVersion")
        if not isinstance(negotiated, str) or not negotiated:
            raise RuntimeError("initialize returned no protocolVersion")
        self.protocol = negotiated
        notification = {
            "jsonrpc": "2.0",
            "method": "notifications/initialized",
        }
        accepted = self._post(notification, include_protocol_header=True)
        if accepted.status != 202 or accepted.body is not None:
            raise RuntimeError(
                "initialized notification expected HTTP 202 with no body, "
                f"received {accepted.status}"
            )
        return initialized

    def request(
        self,
        method: str,
        params: dict[str, Any],
        *,
        include_protocol_header: bool = True,
    ) -> McpResponse:
        with self._request_id_lock:
            request_id = self.next_id
            self.next_id += 1
        payload = {
            "jsonrpc": "2.0",
            "id": request_id,
            "method": method,
            "params": params,
        }
        response = self._post(payload, include_protocol_header)
        if response.body is None:
            raise RuntimeError(f"{method} returned HTTP {response.status} with no JSON body")
        if response.body.get("id") != request_id:
            raise RuntimeError(
                f"{method} response id {response.body.get('id')!r} "
                f"did not match request id {request_id}"
            )
        return response

    def call_tool(self, name: str, arguments: dict[str, Any]) -> dict[str, Any]:
        return require_result(
            self.request(
                "tools/call",
                {"name": name, "arguments": arguments},
            )
        )

    def _post(self, payload: dict[str, Any], include_protocol_header: bool) -> McpResponse:
        encoded = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        headers = {
            "Accept": "application/json, text/event-stream",
            "Content-Type": "application/json",
        }
        if include_protocol_header:
            headers["MCP-Protocol-Version"] = self.protocol
        request = urllib.request.Request(self.url, data=encoded, headers=headers, method="POST")
        self._record("request", {"url": self.url, "headers": headers, "body": payload})
        try:
            with urllib.request.urlopen(request, timeout=15) as raw:
                response = self._decode_response(raw.status, raw.headers, raw.read())
        except urllib.error.HTTPError as error:
            response = self._decode_response(error.code, error.headers, error.read())
        self._record(
            "response",
            {"status": response.status, "headers": response.headers, "body": response.body},
        )
        return response

    @staticmethod
    def _decode_response(status: int, raw_headers: Any, body: bytes) -> McpResponse:
        headers = {name.lower(): value for name, value in raw_headers.items()}
        decoded: dict[str, Any] | None = None
        if body:
            parsed = json.loads(body.decode("utf-8"))
            if not isinstance(parsed, dict):
                raise RuntimeError("MCP response body was not a JSON object")
            decoded = parsed
        return McpResponse(status, headers, decoded)

    def _record(self, direction: str, value: dict[str, Any]) -> None:
        if self.transcript is None:
            return
        with self._transcript_lock:
            self.transcript.parent.mkdir(parents=True, exist_ok=True)
            with self.transcript.open("a", encoding="utf-8") as output:
                output.write(json.dumps({"direction": direction, **value}, sort_keys=True))
                output.write("\n")


def require_result(response: McpResponse) -> dict[str, Any]:
    if response.status != 200 or response.body is None:
        raise RuntimeError(f"MCP request failed with HTTP {response.status}: {response.body}")
    if "error" in response.body:
        raise RuntimeError(f"MCP JSON-RPC error: {response.body['error']}")
    result = response.body.get("result")
    if not isinstance(result, dict):
        raise RuntimeError("MCP response returned no object result")
    return result


def structured(result: dict[str, Any]) -> dict[str, Any]:
    value = result.get("structuredContent")
    if not isinstance(value, dict):
        raise RuntimeError("tool returned no structuredContent object")
    return value


def text_content(result: dict[str, Any]) -> str:
    for item in result.get("content", []):
        if isinstance(item, dict) and item.get("type") == "text":
            value = item.get("text")
            if isinstance(value, str):
                return value
    raise RuntimeError("tool returned no text content")


def parse_arguments_json(value: str) -> dict[str, Any]:
    parsed = json.loads(value)
    if not isinstance(parsed, dict):
        raise argparse.ArgumentTypeError("tool arguments must be a JSON object")
    return parsed


def save_inline_image(result: dict[str, Any], output: pathlib.Path | None) -> dict[str, Any]:
    images = [
        item
        for item in result.get("content", [])
        if isinstance(item, dict) and item.get("type") == "image"
    ]
    if not images:
        if output is not None:
            raise RuntimeError("tool returned no inline image")
        return result
    image = images[-1]
    encoded = image.get("data")
    if not isinstance(encoded, str):
        raise RuntimeError("inline image content has no base64 data")
    raw = base64.b64decode(encoded, validate=True)
    if output is not None:
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(raw)
    cleaned = dict(result)
    cleaned["content"] = [
        {
            "type": "image",
            "mimeType": image.get("mimeType"),
            "bytes": len(raw),
            "savedTo": str(output) if output is not None else None,
        }
        if item is image
        else item
        for item in result.get("content", [])
    ]
    return cleaned


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Attach to the Orb perf-debug MCP server.")
    parser.add_argument("--url", default=DEFAULT_URL)
    parser.add_argument("--transcript", type=pathlib.Path)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("doctor")
    commands.add_parser("tools")
    commands.add_parser("resources")
    commands.add_parser("ping")
    call = commands.add_parser("call")
    call.add_argument("name")
    call.add_argument("--arguments", default={}, type=parse_arguments_json)
    call.add_argument("--image-out", type=pathlib.Path)
    commands.add_parser("measure-reads")
    commands.add_parser("catalog")
    commands.add_parser("continue")
    tooltips = commands.add_parser("tooltips")
    tooltips.add_argument("--offset", type=int, default=0)
    tooltips.add_argument("--limit", type=int, default=25)

    screenshot = commands.add_parser("screenshot")
    screenshot.add_argument("--save", action="store_true")
    screenshot.add_argument("--output", type=pathlib.Path, required=True)

    navigate = commands.add_parser("navigate")
    navigate.add_argument("screen")
    navigate.add_argument("--subtab")
    navigate.add_argument("--uuid")
    navigate.add_argument("--capture", type=pathlib.Path)

    tooltip = commands.add_parser("tooltip")
    tooltip.add_argument("path")

    purchase = commands.add_parser("purchase")
    purchase.add_argument("uuid")
    purchase.add_argument("--amount", type=int, default=1)

    cast = commands.add_parser("cast")
    cast.add_argument("slot_index", type=int)
    cast.add_argument("--mode", choices=("fire", "release", "toggle_off"), default="fire")

    agromancy = commands.add_parser("agromancy")
    agromancy.add_argument(
        "mode",
        choices=(
            "add_plot_action",
            "remove_plot_action",
            "add_element",
            "remove_element",
            "add_element_action",
            "remove_element_action",
        ),
    )
    agromancy.add_argument("uuid")
    agromancy.add_argument("--action-uuid")
    agromancy.add_argument("--amount", type=int, default=1)

    concept = commands.add_parser("concept-add")
    concept.add_argument("uuid")

    spell = commands.add_parser("spell-level")
    spell.add_argument("uuid")

    resource = commands.add_parser("resource")
    resource.add_argument("uri")
    return parser


def doctor(client: GameMcpClient, initialized: dict[str, Any]) -> dict[str, Any]:
    tools = require_result(client.request("tools/list", {}))
    health = text_content(client.call_tool("suite_health", {}))
    world = structured(client.call_tool("world_overview", {}))
    names = [item.get("name") for item in tools.get("tools", [])]
    required = {
        "world_overview",
        "trace_health",
        "game_purchase",
        "game_cast",
        "game_agromancy",
        "game_screenshot",
        "game_continue",
        "game_screen_catalog",
        "game_navigate",
        "game_screen_elements",
        "game_tooltip",
    }
    missing = sorted(required.difference(names))
    if missing:
        raise RuntimeError("doctor: required tools missing: " + ", ".join(missing))
    if "action_receipt" in names:
        raise RuntimeError("doctor: removed action_receipt tool is still exposed")
    if "decision_journal" in names:
        raise RuntimeError("doctor: renamed decision_journal tool is still exposed")
    return {
        "status": "ok",
        "endpoint": client.url,
        "serverInfo": initialized.get("serverInfo"),
        "toolCount": len(names),
        "health": health,
        "world": world,
    }


def observe_rows(
    client: GameMcpClient,
    category: str,
    *,
    limit: int = 200,
) -> dict[str, Any]:
    value = structured(
        client.call_tool("world_list", {"category": category, "limit": limit})
    )
    if not isinstance(value.get("rows"), list):
        raise RuntimeError(f"{category} is not available: {value}")
    return value


def observe_and_purchase(client: GameMcpClient, args: argparse.Namespace) -> dict[str, Any]:
    observed = structured(
        client.call_tool("world_search", {"query": args.uuid, "limit": 50})
    )
    rows = observed.get("rows")
    if not isinstance(rows, list):
        raise RuntimeError("purchase search returned no rows")
    target = next(
        (
            row
            for row in rows
            if isinstance(row, dict)
            and row.get("category") in {"structures", "upgrades"}
        ),
        None,
    )
    if target is None:
        raise RuntimeError("published purchase target is absent")
    explanation = structured(
        client.call_tool("explain_entity", {"uuid": args.uuid})
    )
    if explanation.get("status") != "available":
        raise RuntimeError(f"purchase explanation is unavailable: {explanation}")
    action = {"uuid": args.uuid, "amount": args.amount}
    terminal = structured(client.call_tool("game_purchase", action))
    return {
        "observed": {"target": target, "explanation": explanation},
        "terminal": terminal,
    }


def observe_and_cast(client: GameMcpClient, args: argparse.Namespace) -> dict[str, Any]:
    observed = observe_rows(client, "spell-slots")
    slot = next(
        (
            row
            for row in observed["rows"]
            if isinstance(row, dict) and row.get("slotIndex") == args.slot_index
        ),
        None,
    )
    if not isinstance(slot, dict) or not slot.get("occupied"):
        raise RuntimeError(f"spell slot {args.slot_index} is not occupied")
    recipe_reference = slot.get("spellRecipe")
    recipe = recipe_reference.get("uuid") if isinstance(recipe_reference, dict) else None
    if not isinstance(recipe, str):
        raise RuntimeError("occupied slot has no named spellRecipe reference")
    action = {
        "mode": args.mode,
        "slotIndex": args.slot_index,
        "uuid": recipe,
    }
    return {
        "observed": slot,
        "terminal": structured(client.call_tool("game_cast", action)),
    }


def run_agromancy(client: GameMcpClient, args: argparse.Namespace) -> dict[str, Any]:
    action = {"mode": args.mode, "uuid": args.uuid, "amount": args.amount}
    action_modes = {
        "add_plot_action",
        "remove_plot_action",
        "add_element_action",
        "remove_element_action",
    }
    if args.mode in action_modes:
        if not args.action_uuid:
            raise RuntimeError(f"agromancy {args.mode} requires --action-uuid")
        action["actionUuid"] = args.action_uuid
    elif args.action_uuid:
        raise RuntimeError(f"agromancy {args.mode} does not accept --action-uuid")
    return structured(client.call_tool("game_agromancy", action))


def observe_and_concept(client: GameMcpClient, args: argparse.Namespace) -> dict[str, Any]:
    observed = observe_rows(client, "concept-recipes")
    recipe = next(
        (
            row
            for row in observed["rows"]
            if isinstance(row, dict)
            and row.get("uuid") == args.uuid
        ),
        None,
    )
    if not isinstance(recipe, dict) or recipe.get("canAdd") is not True:
        raise RuntimeError(f"concept {args.uuid} is not addable")
    action = {"mode": "add", "uuid": args.uuid, "amount": 1}
    return {
        "observed": recipe,
        "terminal": structured(client.call_tool("game_concept", action)),
    }


def observe_and_spell_level(client: GameMcpClient, args: argparse.Namespace) -> dict[str, Any]:
    observed = structured(
        client.call_tool(
            "world_get",
            {"category": "spell-recipes", "uuids": [args.uuid]},
        )
    )
    results = observed.get("results")
    row_result = results[0] if isinstance(results, list) and results else None
    if not isinstance(row_result, dict) or not isinstance(row_result.get("row"), dict):
        raise RuntimeError(f"spell recipe is unavailable: {observed}")
    action = {"mode": "single", "uuid": args.uuid}
    return {
        "observed": row_result["row"],
        "terminal": structured(client.call_tool("game_spell_mastery", action)),
    }


def measure_reads(client: GameMcpClient) -> dict[str, Any]:
    calls: list[tuple[str, dict[str, Any]]] = [
        ("world_overview", {}),
        ("world_categories", {}),
        ("world_list", {"category": "resources", "limit": 25}),
        ("suite_health", {}),
        ("suite_configuration", {}),
        ("trace_health", {}),
        ("game_probe", {"probe": "runtime"}),
        ("game_screen_catalog", {}),
        ("game_screen_elements", {"offset": 0, "limit": 25}),
    ]
    resource_list = structured(client.call_tool("world_list", {"category": "resources", "limit": 1}))
    rows = resource_list.get("rows")
    if isinstance(rows, list) and rows and isinstance(rows[0], dict):
        uuid = rows[0].get("uuid")
        if isinstance(uuid, str):
            calls.insert(3, ("world_get", {"category": "resources", "uuids": [uuid]}))
            calls.insert(4, ("world_search", {"query": uuid, "limit": 20}))

    measurements = []
    for name, arguments in calls:
        result = client.call_tool(name, arguments)
        encoded = json.dumps(result, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
        structured_value = result.get("structuredContent")
        measurements.append(
            {
                "tool": name,
                "arguments": arguments,
                "bytes": len(encoded),
                "approxTokens": math.ceil(len(encoded) / 4),
                "status": structured_value.get("status")
                if isinstance(structured_value, dict)
                else "text",
            }
        )
    tooltip_catalog = structured(
        client.call_tool("game_screen_elements", {"offset": 0, "limit": 1})
    )
    tooltip_rows = tooltip_catalog.get("rows")
    if isinstance(tooltip_rows, list) and tooltip_rows and isinstance(tooltip_rows[0], dict):
        path = tooltip_rows[0].get("path")
        if isinstance(path, str):
            result = client.call_tool("game_tooltip", {"path": path})
            encoded = json.dumps(result, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
            measurements.append(
                {
                    "tool": "game_tooltip",
                    "arguments": {"path": path},
                    "bytes": len(encoded),
                    "approxTokens": math.ceil(len(encoded) / 4),
                    "status": structured(result).get("status"),
                    "note": "mutates visible tooltip state; measured for completeness",
                }
            )
    return {"measurement": "compact MCP result JSON, UTF-8", "tools": measurements}


def main() -> int:
    args = build_parser().parse_args()
    client = GameMcpClient(args.url, args.transcript)
    try:
        initialized = client.initialize()
        if args.command == "doctor":
            result: Any = doctor(client, initialized)
        elif args.command == "tools":
            result = require_result(client.request("tools/list", {}))
        elif args.command == "resources":
            result = require_result(client.request("resources/list", {}))
        elif args.command == "ping":
            result = require_result(client.request("ping", {}))
        elif args.command == "call":
            result = save_inline_image(
                client.call_tool(args.name, args.arguments),
                args.image_out,
            )
        elif args.command == "measure-reads":
            result = measure_reads(client)
        elif args.command == "catalog":
            result = client.call_tool("game_screen_catalog", {})
        elif args.command == "continue":
            result = client.call_tool("game_continue", {})
        elif args.command == "tooltips":
            result = client.call_tool(
                "game_screen_elements",
                {"offset": args.offset, "limit": args.limit},
            )
        elif args.command == "screenshot":
            result = save_inline_image(
                client.call_tool("game_screenshot", {"save": args.save}),
                args.output,
            )
        elif args.command == "navigate":
            arguments: dict[str, Any] = {"screen": args.screen}
            if args.subtab is not None:
                arguments["subtab"] = args.subtab
            if args.uuid:
                arguments["uuid"] = args.uuid
            if args.capture is not None:
                arguments["capture"] = True
            result = save_inline_image(
                client.call_tool("game_navigate", arguments),
                args.capture,
            )
        elif args.command == "tooltip":
            result = client.call_tool("game_tooltip", {"path": args.path})
        elif args.command == "purchase":
            result = observe_and_purchase(client, args)
        elif args.command == "cast":
            result = observe_and_cast(client, args)
        elif args.command == "agromancy":
            result = run_agromancy(client, args)
        elif args.command == "concept-add":
            result = observe_and_concept(client, args)
        elif args.command == "spell-level":
            result = observe_and_spell_level(client, args)
        elif args.command == "resource":
            result = require_result(client.request("resources/read", {"uri": args.uri}))
        else:
            raise RuntimeError(f"unsupported command: {args.command}")
        print(json.dumps(result, indent=2, sort_keys=True))
        return 0
    except (OSError, ValueError, RuntimeError) as error:
        print(f"game-mcp-client: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
