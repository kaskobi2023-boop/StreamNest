import importlib.util
import pathlib
import unittest
from unittest.mock import patch

from yt_dlp import YoutubeDL
from yt_dlp.utils import ExtractorError

path = pathlib.Path(__file__).resolve().parents[1] / 'ChzzkDownloader/Tools/yt-dlp-plugins/streamnest/yt_dlp_plugins/extractor/streamnest_chzzk.py'
spec = importlib.util.spec_from_file_location('chzzk_clip_under_test', path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
ClipIE = module.StreamNestCHZZKClipIE


class ChzzkClipTests(unittest.TestCase):
    def fixture(self):
        return {'code': 200, 'content': {'clipUID': 'AbCdEf1234', 'videoId': 'fixture-video',
            'clipTitle': 'Fixture clip', 'duration': 60}}, {'code': 200, 'content': {
                'contentId': 'AbCdEf1234', 'videoId': 'fixture-video', 'inKey': 'fixture-key',
                'ownerChannel': {'channelName': 'Fixture channel'}}}

    def extract(self, responses):
        with YoutubeDL({'quiet': True}) as ydl:
            ie = ClipIE(ydl)
            with patch.object(ie, '_download_json', side_effect=responses), patch.object(ie,
                    '_extract_mpd_formats_and_subtitles', return_value=([{'height': 1280}], {})) as mpd:
                result = ie._real_extract('https://chzzk.naver.com/clips/AbCdEf1234')
                return result, mpd.call_args

    def test_plugin_has_its_own_identity(self):
        self.assertIsNone(getattr(ClipIE, 'PLUGIN_NAME', None))
        self.assertEqual('StreamNestCHZZKClip', ClipIE.ie_key())
        self.assertTrue(ClipIE.suitable('https://chzzk.naver.com/clips/AbCdEf1234'))
        self.assertFalse(ClipIE.suitable('https://chzzk.naver.com/video/123'))

    def test_metadata_and_xml_manifest_request(self):
        entry, call = self.extract(self.fixture())
        self.assertEqual('AbCdEf1234', entry['id'])
        self.assertEqual(60, entry['duration'])
        self.assertEqual('Fixture channel', entry['channel'])
        self.assertEqual('application/dash+xml', call.kwargs['headers']['Accept'])
        self.assertEqual('fixture-key', call.kwargs['query']['key'])
        self.assertNotIn('Accept', entry['formats'][0]['http_headers'])
        self.assertNotIn('fixture-key', str(entry))

    def test_changed_clip_is_rejected(self):
        detail, play = self.fixture()
        play['content']['contentId'] = 'other-clip'
        with self.assertRaisesRegex(ExtractorError, 'identity changed'):
            self.extract([detail, play])

    def test_changed_media_is_rejected(self):
        detail, play = self.fixture()
        play['content']['videoId'] = 'other-media'
        with self.assertRaisesRegex(ExtractorError, 'identity changed'):
            self.extract([detail, play])

    def test_login_required_does_not_request_media(self):
        with self.assertRaisesRegex(ExtractorError, 'Log in'):
            self.extract([{'code': 401, 'content': None}])

    def test_adult_authorization_is_preserved(self):
        detail, play = self.fixture()
        detail['content'].update(adult=True, userAdultStatus='NOT_LOGIN_USER')
        with self.assertRaisesRegex(ExtractorError, 'authorized adult account'):
            self.extract([detail, play])

    def test_deleted_clip_is_rejected(self):
        with self.assertRaisesRegex(ExtractorError, 'unavailable'):
            self.extract([{'code': 404, 'content': None}])

    def test_missing_playback_key_is_rejected(self):
        detail, play = self.fixture()
        play['content'].pop('inKey')
        with self.assertRaisesRegex(ExtractorError, 'playback key'):
            self.extract([detail, play])


if __name__ == '__main__':
    unittest.main()
