// 얼굴 사진 올리기(ADR 0078, R-202): 고른 사진의 가운데를 네모로 잘라 320px JPEG 로 줄인 뒤 바로 올린다.
// 줄이기에 실패하면(오래된 브라우저) 고른 파일 그대로 올리고, 크기는 서버가 400KB 로 막는다.
(function () {
  var SIZE = 320;
  document.querySelectorAll('form[data-photo]').forEach(function (form) {
    var input = form.querySelector('input[type=file]');
    if (!input) return;
    input.addEventListener('change', function () {
      var file = input.files && input.files[0];
      if (!file) return;
      var url = URL.createObjectURL(file), img = new Image();
      img.onload = function () {
        var s = Math.min(img.naturalWidth, img.naturalHeight), c = document.createElement('canvas');
        c.width = c.height = SIZE;
        c.getContext('2d').drawImage(img, (img.naturalWidth - s) / 2, (img.naturalHeight - s) / 2, s, s, 0, 0, SIZE, SIZE);
        URL.revokeObjectURL(url);
        c.toBlob(function (blob) {
          try {
            var dt = new DataTransfer();
            dt.items.add(new File([blob], 'photo.jpg', { type: 'image/jpeg' }));
            input.files = dt.files;
          } catch (e) { /* 바꿀 수 없으면 원래 파일 */ }
          form.submit();
        }, 'image/jpeg', 0.85);
      };
      img.onerror = function () { URL.revokeObjectURL(url); form.submit(); };
      img.src = url;
    });
  });
})();
