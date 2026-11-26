"""Keep a guarded optional downloader from taking down local playback."""


def start_downloader(script, on_message):
    errors = []

    def message(value, data):
        if value.get('type') == 'error':
            errors.append(value.get('description', 'Downloader initialization failed'))
        on_message(value, data)

    script.on('message', message)
    try:
        script.load()
        if errors:
            raise RuntimeError(errors[0])
        script.exports_sync.seedexisting()
        return True, None
    except Exception as error:
        reason = errors[0] if errors else str(error)
        # These checks are before any hooks/jobs: unloading owns no native jobs.
        if 'Unexpected download entry' not in reason and 'Unsupported image' not in reason:
            raise
        script.unload()
        return False, reason
