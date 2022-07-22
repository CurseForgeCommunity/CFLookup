// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.


(function () {
    let fileIdInput = document.querySelector('#fileSearchField');

    if (fileIdInput) {
        fileIdInput.addEventListener('wheel', function (evt) {
            evt.preventDefault();
        });
    }
})();