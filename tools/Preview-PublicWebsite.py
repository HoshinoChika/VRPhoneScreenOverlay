"""Loopback-only static preview; does not publish or accept uploads."""
import argparse
from functools import partial
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--directory", required=True)
parser.add_argument("--port", type=int, default=5410)
args = parser.parse_args()
root = Path(args.directory).resolve(strict=True)
handler = partial(SimpleHTTPRequestHandler, directory=str(root))
ThreadingHTTPServer(("127.0.0.1", args.port), handler).serve_forever()
