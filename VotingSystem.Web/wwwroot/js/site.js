// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Global UI behaviors for the election platform.
(() => {
  document.addEventListener('DOMContentLoaded', () => {
    // Mobile admin sidebar toggle
    const adminSidebar = document.querySelector('.admin-sidebar');
    if (adminSidebar) {
      const toggle = document.createElement('button');
      toggle.className = 'btn-ghost d-md-none';
      toggle.innerHTML = '<i class="bi bi-list"></i>';
      toggle.style.marginRight = '0.5rem';
      document.body.prepend(toggle);
      toggle.addEventListener('click', () => { adminSidebar.style.display = adminSidebar.style.display === 'none' ? 'flex' : 'none'; });
    }

    // Simple search filter helper for any table with data-search-input
    document.querySelectorAll('[data-search-input]').forEach(input => {
      const selector = input.dataset.searchTarget;
      const rows = document.querySelectorAll(selector);
      input.addEventListener('input', () => {
        const q = input.value.trim().toLowerCase();
        rows.forEach(r => r.style.display = r.textContent.toLowerCase().includes(q) ? '' : 'none');
      });
    });

    // Vote modal behavior (keeps previous functionality)
    const voteButtons = document.querySelectorAll('.vote-action-button');
    const voteModalElement = document.getElementById('voteConfirmModal');
    const voteForm = document.getElementById('voteForm');
    const voteCandidateName = document.getElementById('voteCandidateName');
    const voteCandidateId = document.getElementById('selectedCandidateId');
    const voteConfirmAction = document.getElementById('confirmVoteAction');

    if (voteButtons.length && voteModalElement && voteForm && voteCandidateName && voteCandidateId && voteConfirmAction) {
      const voteModal = new bootstrap.Modal(voteModalElement);
      voteButtons.forEach(btn => btn.addEventListener('click', () => {
        const candidateId = btn.dataset.candidateId;
        const candidateName = btn.dataset.candidateName;
        voteCandidateName.textContent = candidateName;
        voteCandidateId.value = candidateId;
        voteModal.show();
      }));

      voteConfirmAction.addEventListener('click', () => voteForm.submit());
    }

    // Tooltips
    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(el => new bootstrap.Tooltip(el));
  });
})();
