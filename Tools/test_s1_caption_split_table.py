"""The committed Illustrated split table must match the committed packages (regenerate with s1_caption_split_table.py)."""
import subprocess, sys, unittest
from pathlib import Path

class CaptionSplitTable(unittest.TestCase):
    def test_table_is_up_to_date(self):
        p = subprocess.run([sys.executable, str(Path(__file__).with_name('s1_caption_split_table.py')), '--check'], capture_output=True, text=True)
        self.assertEqual(p.returncode, 0, p.stdout + p.stderr)

if __name__ == '__main__':
    unittest.main()
