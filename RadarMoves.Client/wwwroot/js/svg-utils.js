// Function to convert client coordinates to SVG coordinates
window.getSVGCoordinates = function (svgElement, clientX, clientY) {
    if (!svgElement) {
        return null;
    }

    try {
        // Create an SVG point for coordinate transformation
        const svgPoint = svgElement.createSVGPoint();
        
        // Set the point in screen/client coordinates
        svgPoint.x = clientX;
        svgPoint.y = clientY;
        
        // Get the current transformation matrix from screen to SVG coordinates
        const ctm = svgElement.getScreenCTM();
        
        if (ctm) {
            // Transform the point from screen coordinates to SVG coordinates
            const invertedCTM = ctm.inverse();
            const transformedPoint = svgPoint.matrixTransform(invertedCTM);
            return [transformedPoint.x, transformedPoint.y];
        }
        
        // Fallback: calculate relative to SVG element's bounding box
        const rect = svgElement.getBoundingClientRect();
        const x = clientX - rect.left;
        const y = clientY - rect.top;
        return [x, y];
    } catch (error) {
        console.error('Error in getSVGCoordinates:', error);
        // Fallback: return coordinates relative to SVG element
        const rect = svgElement.getBoundingClientRect();
        return [clientX - rect.left, clientY - rect.top];
    }
};

