"""SOOP completion safeguards; extraction and authentication stay upstream."""
from yt_dlp.extractor.afreecatv import AfreecaTVIE
from yt_dlp.utils import ExtractorError


class _StreamNestSoopIE(AfreecaTVIE, plugin_name='streamnest_soop'):
    def _call_api(self, endpoint, display_id, *args, **kwargs):
        result = super()._call_api(endpoint, display_id, *args, **kwargs)
        data = result.get('data') if isinstance(result, dict) else None
        if isinstance(data, dict) and data.get('adult_status') == 'notLogin':
            # Upstream may return only unrestricted parts of a restricted VOD.
            # Never present that subset as the whole video. The user must log in.
            self.raise_login_required(
                'Log in to SOOP with an account authorized to view the complete VOD', method='cookies')
        return result

    def _real_extract(self, url):
        result = super()._real_extract(url)
        expected = self._configuration_arg('expected_ids', ie_key='soop', casesense=True)
        if expected:
            parts = result.get('entries') if result.get('_type') == 'multi_video' else [result]
            actual = [part.get('id') for part in (parts or []) if isinstance(part, dict)]
            if actual != expected:
                raise ExtractorError(
                    'SOOP VOD parts changed since analysis. Analyze the video again; no partial result was accepted.',
                    expected=True)
        return result
