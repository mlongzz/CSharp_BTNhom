const passwordInput = document.getElementById("password");
const toggleButton = document.getElementById("toggle-password");
toggleButton.addEventListener("click", () => {
    const visible = passwordInput.type === "password";
    passwordInput.type = visible ? "text" : "password";
    toggleButton.textContent = visible ? "Ẩn" : "Hiện";
    toggleButton.setAttribute("aria-label", visible ? "Ẩn mật khẩu" : "Hiện mật khẩu");
});

const confirmation = document.getElementById("confirmPassword");
if (confirmation) {
    function checkConfirmation() {
        const mismatch = confirmation.value && confirmation.value !== passwordInput.value;
        confirmation.setCustomValidity(mismatch ? "Mật khẩu xác nhận chưa khớp." : "");
    }
    passwordInput.addEventListener("input", checkConfirmation);
    confirmation.addEventListener("input", checkConfirmation);
}
