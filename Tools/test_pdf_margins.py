"""The Reader's 100% PDF crop must stay inside the white margin of every committed portrait page."""
import subprocess, sys, unittest
from pathlib import Path

class PdfMargins(unittest.TestCase):
    def test_crop_never_cuts_ink(self):
        p = subprocess.run([sys.executable, str(Path(__file__).with_name('pdf_margins.py')), '--check'], capture_output=True, text=True)
        self.assertEqual(p.returncode, 0, p.stdout + p.stderr)

if __name__ == '__main__':
    unittest.main()
