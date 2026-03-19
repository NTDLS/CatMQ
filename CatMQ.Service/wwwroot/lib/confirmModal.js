function showConfirmModal(message) {
    return new Promise(resolve => {

        const msgEl = document.getElementById("confirmModalMessage");
        const okBtn = document.getElementById("confirmOk");
        const cancelBtn = document.getElementById("confirmCancel");
        const modalEl = document.getElementById("confirmModal");

        msgEl.textContent = message;

        const bsModal = new bootstrap.Modal(modalEl);

        const cleanup = () => {
            okBtn.onclick = null;
            cancelBtn.onclick = null;
            modalEl.removeEventListener("hidden.bs.modal", cleanup);
        };

        okBtn.onclick = () => {
            cleanup();
            bsModal.hide();
            resolve(true);
        };

        cancelBtn.onclick = () => {
            cleanup();
            resolve(false);
        };

        modalEl.addEventListener("hidden.bs.modal", cleanup);

        bsModal.show();
    });
}
