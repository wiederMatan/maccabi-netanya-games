const stars = parseInt(localStorage.getItem('maccabi-netanya-stars') || '0');
document.getElementById('total-stars').textContent = stars;
