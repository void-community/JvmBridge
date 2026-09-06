"""Regression tests for process isolation in the real-JVM test runner."""
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from test_agent import run

class ProcessTests(unittest.TestCase):
    def test_success_retains_output(self):
        with tempfile.TemporaryDirectory() as directory:
            log=Path(directory)/'output.log'
            result=run([sys.executable,'-c','print("fixture-ok")'],log)
            self.assertEqual(result.returncode,0)
            self.assertIn('fixture-ok',log.read_text())

    def test_timeout_terminates_process_and_preserves_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            log=Path(directory)/'timeout.log'
            started=time.monotonic()
            with self.assertRaisesRegex(RuntimeError,'Timed out'):
                run([sys.executable,'-u','-c','import time; print("started"); time.sleep(60)'],log,timeout=0.5)
            self.assertLess(time.monotonic()-started,20)
            self.assertIn('started',log.read_text())

if __name__=='__main__': unittest.main()
