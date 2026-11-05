#!/usr/bin/env python3
"""A small WebDriverAgent client for an iOS device: status, session, tap, type, find, click, screenshot, source.

WebDriverAgent (Appium's, https://github.com/appium/WebDriverAgent, cloned beside this repo) runs on the device as an
XCUITest runner and takes commands over HTTP; this drives the demo through it the way adb and uiautomator drive it on
Android (README.md, "iOS"). Start the runner (about a minute; the command keeps running while the server is up):

  cd ../WebDriverAgent && xcodebuild -project WebDriverAgent.xcodeproj -scheme WebDriverAgentRunner \
    -destination 'id=<device id>' -allowProvisioningUpdates DEVELOPMENT_TEAM=<team> \
    PRODUCT_BUNDLE_IDENTIFIER=com.emgu.WebDriverAgentRunner CODE_SIGN_STYLE=Automatic test   # first time: build it too

The server listens on port 8100 on the device. Over USB, reach it at the device's CoreDevice tunnel address, from
`xcrun devicectl device info details --device <id>` ("Tunnel IP Address", an IPv6 address, so in brackets:
http://[fdb3:...::1]:8100), with no port forwarder; over Wi-Fi at the address `status` prints under "ios"/"ip".
The device needs Developer Mode and, on iOS 17 and later, Settings > Developer > UI Automation.

After `setvalue` into the message field, wait half a second before `click Send`: a click during the keyboard's
rise was lost on the iPad.

Usage: ios_wda.py <server> <command> [args]
  status
  session <bundleId>            -> prints the session id (kept in ~/.wda_session)
  tap <x> <y>
  type <text>                   (types into the focused field)
  find <accessibility id|name>  -> prints the element id and its rect
  click <accessibility id|name>
  setvalue <accessibility id|name> <text>
  screenshot <file.png>
  source [file.xml]
  home
  launch <bundleId>
"""
import base64, json, os, sys, urllib.request

SESSION_FILE = os.path.expanduser("~/.wda_session")


def call(server, method, path, body=None, timeout=60):
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(server + path, data=data, method=method, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode())


def session_id(server):
    if os.path.exists(SESSION_FILE):
        return open(SESSION_FILE).read().strip()
    status = call(server, "GET", "/status")
    sid = status.get("sessionId")
    if not sid:
        raise SystemExit("no session: run 'session <bundleId>' first")
    return sid


def find(server, sid, name):
    for using in ("accessibility id", "name", "link text"):
        try:
            result = call(server, "POST", f"/session/{sid}/element", {"using": using, "value": name})
            element = result["value"]
            if "ELEMENT" in element:
                return element["ELEMENT"]
            for key, value in element.items():
                if key.startswith("element"):
                    return value
        except Exception:
            continue
    # A substring match through the class chain, as a fallback.
    result = call(server, "POST", f"/session/{sid}/element",
                  {"using": "class chain", "value": f"**/*[`label CONTAINS '{name}' OR name CONTAINS '{name}'`]"})
    element = result["value"]
    return element.get("ELEMENT") or next(v for k, v in element.items() if k.startswith("element"))


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    server, command, args = sys.argv[1].rstrip("/"), sys.argv[2], sys.argv[3:]
    if command == "status":
        print(json.dumps(call(server, "GET", "/status")["value"], indent=1)[:1500])
        return 0
    if command == "session":
        result = call(server, "POST", "/session", {"capabilities": {"alwaysMatch": {"bundleId": args[0], "shouldWaitForQuiescence": False}}}, timeout=120)
        sid = result.get("sessionId") or result["value"]["sessionId"]
        open(SESSION_FILE, "w").write(sid)
        print(sid)
        return 0
    sid = session_id(server)
    if command == "tap":
        x, y = float(args[0]), float(args[1])
        call(server, "POST", f"/session/{sid}/actions", {"actions": [{"type": "pointer", "id": "finger", "parameters": {"pointerType": "touch"}, "actions": [
            {"type": "pointerMove", "duration": 0, "x": x, "y": y}, {"type": "pointerDown", "button": 0}, {"type": "pause", "duration": 80}, {"type": "pointerUp", "button": 0}]}]})
        print("tapped", x, y)
    elif command == "type":
        call(server, "POST", f"/session/{sid}/wda/keys", {"value": list(args[0])})
        print("typed", len(args[0]), "chars")
    elif command == "find":
        element = find(server, sid, args[0])
        rect = call(server, "GET", f"/session/{sid}/element/{element}/rect")["value"]
        print(element, rect)
    elif command == "click":
        element = find(server, sid, args[0])
        call(server, "POST", f"/session/{sid}/element/{element}/click", {})
        print("clicked", args[0])
    elif command == "setvalue":
        element = find(server, sid, args[0])
        call(server, "POST", f"/session/{sid}/element/{element}/value", {"value": list(args[1])})
        print("set", args[0])
    elif command == "screenshot":
        png = base64.b64decode(call(server, "GET", f"/session/{sid}/screenshot")["value"])
        open(args[0], "wb").write(png)
        print(args[0], len(png), "bytes")
    elif command == "source":
        xml = call(server, "GET", f"/session/{sid}/source")["value"]
        if args:
            open(args[0], "w").write(xml); print(args[0], len(xml), "chars")
        else:
            print(xml[:4000])
    elif command == "home":
        call(server, "POST", f"/wda/homescreen", {})
        print("home")
    elif command == "launch":
        call(server, "POST", f"/session/{sid}/wda/apps/launch", {"bundleId": args[0]})
        print("launched", args[0])
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
