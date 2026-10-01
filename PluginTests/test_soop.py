import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

from yt_dlp import YoutubeDL
from yt_dlp.extractor.afreecatv import AfreecaTVIE
from yt_dlp.utils import ExtractorError


PLUGIN = Path(__file__).resolve().parents[1] / 'ChzzkDownloader/Tools/yt-dlp-plugins/streamnest/yt_dlp_plugins/extractor/streamnest_soop.py'
spec = importlib.util.spec_from_file_location('soop_guard_test', PLUGIN)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class SoopSafeguardTests(unittest.TestCase):
    def extractor(self, ids=None):
        return module._StreamNestSoopIE(YoutubeDL({
            'quiet': True, 'extractor_args': {'soop': {'expected_ids': ids or []}}}))

    def test_public_api_is_unchanged(self):
        data = {'data': {'files': [{'file': 'https://example.test/v.m3u8'}]}}
        with patch.object(AfreecaTVIE, '_call_api', return_value=data):
            self.assertEqual(self.extractor()._call_api('station/video/a/view', '1'), data)

    def test_partial_restricted_result_requires_login(self):
        data = {'data': {'adult_status': 'notLogin', 'files': [{'file': 'https://example.test/part.m3u8'}]}}
        with patch.object(AfreecaTVIE, '_call_api', return_value=data):
            with self.assertRaises(ExtractorError):
                self.extractor()._call_api('station/video/a/view', '1')

    def test_same_parts_are_accepted(self):
        data = {'_type': 'multi_video', 'entries': [{'id': 'p1'}, {'id': 'p2'}]}
        with patch.object(AfreecaTVIE, '_real_extract', return_value=data):
            self.assertEqual(self.extractor(['p1', 'p2'])._real_extract('https://vod.sooplive.com/player/1'), data)

    def test_missing_reordered_or_extra_parts_are_rejected(self):
        for ids in (['p1'], ['p2', 'p1'], ['p1', 'p2', 'p3']):
            with self.subTest(ids=ids):
                data = {'_type': 'multi_video', 'entries': [{'id': item} for item in ids]}
                with patch.object(AfreecaTVIE, '_real_extract', return_value=data):
                    with self.assertRaises(ExtractorError):
                        self.extractor(['p1', 'p2'])._real_extract('https://vod.sooplive.com/player/1')

    def test_single_vod_identity_is_checked(self):
        with patch.object(AfreecaTVIE, '_real_extract', return_value={'id': 'single'}):
            self.assertEqual(self.extractor(['single'])._real_extract('https://vod.sooplive.com/player/1')['id'], 'single')
            with self.assertRaises(ExtractorError):
                self.extractor(['other'])._real_extract('https://vod.sooplive.com/player/1')


if __name__ == '__main__':
    unittest.main()
