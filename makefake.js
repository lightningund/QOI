const hex = (num) => (num % 256).toString(16).padStart(2, "0");

function make_fake(width, height) {
	let str = hex(width) + hex(height);
	for (let i = 0; i < width; ++i) {
		for (let j = 0; j < height; ++j) {
			str += "00" + hex(i * 16) + hex(j * 16);
		}
	}

	return str;
}

console.log(make_fake(16, 16));