import json
import unittest
from requests.structures import CaseInsensitiveDict
import unity_mcp

class Reply:
    def __init__(self, text, content_type='application/json'):
        self.text=text
        self.status_code=200
        self.headers=CaseInsensitiveDict({'Content-Type':content_type})
    def raise_for_status(self):
        pass
    def json(self):
        return json.loads(self.text)

class DecodeChecks(unittest.TestCase):
    def test_json(self):
        self.assertEqual(unity_mcp.decode(Reply('{"result":1}')), {'result':1})
    def test_sse(self):
        self.assertEqual(unity_mcp.decode(Reply('event: message\ndata: {"result":2}\n\n','text/event-stream')), {'result':2})
    def test_comment_before_result(self):
        body=': ping - waiting for Unity\n\nevent: message\ndata: {"jsonrpc":"2.0","id":2,"result":{"success":true}}\n\n'
        self.assertTrue(unity_mcp.decode(Reply(body,'text/event-stream'))['result']['success'])
    def test_multiline_data(self):
        body=': keepalive\n\ndata: {"id":2,\ndata: "result":3}\n\n'
        self.assertEqual(unity_mcp.decode(Reply(body,'text/event-stream'))['result'],3)
    def test_empty_response_is_not_completion(self):
        with self.assertRaises(RuntimeError):
            unity_mcp.decode(Reply(''))
    def test_only_keepalive_is_not_completion(self):
        with self.assertRaises(RuntimeError):
            unity_mcp.decode(Reply(': ping\n\n','text/event-stream'))

if __name__=='__main__':
    unittest.main()
