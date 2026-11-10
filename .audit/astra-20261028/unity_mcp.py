"""Bounded local MCP client for S1 release verification. No automatic retries of writes."""
import json
import sys
import requests

URL = "http://127.0.0.1:8080/mcp"


def decode(response):
    response.raise_for_status()
    text = response.text.lstrip('\ufeff').strip()
    if not text:
        raise RuntimeError('MCP response was empty; command completion is unknown')
    lines = text.replace('\r\n', '\n').split('\n')
    if 'event-stream' in response.headers.get('Content-Type', '') or any(line.startswith('data:') for line in lines):
        messages = []
        pending = []
        for line in lines + ['']:
            if line.startswith('data:'):
                pending.append(line[5:].lstrip(' '))
            elif not line and pending:
                messages.append(json.loads('\n'.join(pending)))
                pending = []
        replies = [m for m in messages if isinstance(m, dict) and ('result' in m or 'error' in m)]
        if not replies:
            raise RuntimeError('MCP stream contained no result; command completion is unknown')
        return replies[-1]
    try:
        return response.json()
    except ValueError as error:
        raise RuntimeError('Non-JSON MCP response; completion unknown: ' + repr(text[:240])) from error


def request(method, params=None, timeout=180):
    with requests.Session() as session:
        session.headers.update({"Accept": "application/json, text/event-stream", "Content-Type": "application/json"})
        response = session.post(URL, json={"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "S1-Astra-verification", "version": "1"}}}, timeout=10)
        decode(response)
        if response.headers.get("mcp-session-id"):
            session.headers["mcp-session-id"] = response.headers["mcp-session-id"]
        session.post(URL, json={"jsonrpc": "2.0", "method": "notifications/initialized"}, timeout=10).raise_for_status()
        result = decode(session.post(URL, json={"jsonrpc": "2.0", "id": 2, "method": method, "params": params or {}}, timeout=timeout))
        if result and "error" in result:
            raise RuntimeError(json.dumps(result["error"]))
        return result.get("result", {})


if __name__ == "__main__":
    try:
        mode = sys.argv[1]
        if mode == "code":
            result = request("tools/call", {"name": "execute_code", "arguments": {"action": "execute", "code": sys.argv[2], "safety_checks": True, "compiler": "auto"}})
        elif mode == "call":
            result = request("tools/call", {"name": sys.argv[2], "arguments": json.loads(sys.argv[3])})
        elif mode == "read":
            result = request("resources/read", {"uri": sys.argv[2]})
        elif mode == "resources":
            result = request("resources/list")
        elif mode == "schema":
            all_tools = request("tools/list")
            result = [item for item in all_tools.get("tools", []) if item["name"] in sys.argv[2:]]
        else:
            raise ValueError("mode must be code/call/read/resources/schema")
        print(json.dumps(result, ensure_ascii=True))
        if isinstance(result, dict) and result.get("isError"):
            raise SystemExit(2)
    except Exception as error:
        print(json.dumps({"client_error": str(error)}, ensure_ascii=True))
        raise SystemExit(1)
