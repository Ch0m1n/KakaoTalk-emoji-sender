// Cancellation drops queued work and ignores the result of an active GPU job.
export function requestQueue(handle, fail) {
    const jobs = new Map();
    let chain = Promise.resolve();
    return data => {
        if (data.action === 'cancel') {
            const job = jobs.get(data.cancelId);
            if (job) job.canceled = true;
            return chain;
        }
        const job = { canceled: false };
        jobs.set(data.id, job);
        chain = chain.then(async () => {
            try {
                if (!job.canceled) await handle(data, () => job.canceled);
            } catch (error) {
                if (!job.canceled) fail(data.id, error);
            } finally {
                jobs.delete(data.id);
            }
        });
        return chain;
    };
}
